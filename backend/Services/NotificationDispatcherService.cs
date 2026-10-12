using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using DbaTool.Api.Abstractions;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Services;

public sealed class NotificationDispatcherService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<NotificationDispatcherService> logger) : INotificationDispatcherService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task DispatchExecutionAlertAsync(JobExecution execution, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var profiles = await db.NotificationProfiles
                .AsNoTracking()
                .Where(p => p.IsEnabled)
                .ToListAsync(ct);

            if (profiles.Count == 0) return;

            var isSuccess = string.Equals(execution.Status, "completed", StringComparison.OrdinalIgnoreCase);

            foreach (var profile in profiles)
            {
                if (isSuccess && !profile.NotifyOnSuccess) continue;
                if (!isSuccess && !profile.NotifyOnFailure) continue;

                try
                {
                    await SendExecutionNotificationAsync(profile, execution, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to send execution notification via profile '{ProfileName}' ({Channel})", profile.Name, profile.Channel);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while dispatching execution alerts for execution {ExecutionId}", execution.Id);
        }
    }

    public async Task<bool> SendTestNotificationAsync(NotificationProfile profile, CancellationToken ct = default)
    {
        var dummyExecution = new JobExecution
        {
            Id = 9999,
            JobName = "test-backup-job",
            CommandType = "dump",
            Trigger = "test",
            CliCommand = "dbb dump test-backup-job --dry-run",
            Status = "completed",
            ExitCode = 0,
            StartedAt = DateTime.UtcNow.AddSeconds(-5),
            FinishedAt = DateTime.UtcNow,
            DurationMs = 5000,
            OutputFile = "test_backup_20261012.sql.zst",
            OutputSize = 1048576,
            IsDryRun = true
        };

        return await SendExecutionNotificationAsync(profile, dummyExecution, ct, isTest: true);
    }

    private async Task<bool> SendExecutionNotificationAsync(
        NotificationProfile profile,
        JobExecution execution,
        CancellationToken ct,
        bool isTest = false)
    {
        var channel = profile.Channel.ToLowerInvariant().Trim();
        var client = httpClientFactory.CreateClient("NotificationClient");
        client.Timeout = TimeSpan.FromSeconds(15);

        return channel switch
        {
            "telegram" => await SendTelegramAsync(client, profile, execution, isTest, ct),
            "slack" => await SendSlackAsync(client, profile, execution, isTest, ct),
            "discord" => await SendDiscordAsync(client, profile, execution, isTest, ct),
            "webhook" => await SendWebhookAsync(client, profile, execution, isTest, ct),
            "email" => await SendEmailAsync(profile, execution, isTest, ct),
            _ => throw new InvalidOperationException($"Unsupported notification channel '{profile.Channel}'.")
        };
    }

    private static string FormatBytes(long? bytes)
    {
        if (!bytes.HasValue || bytes.Value <= 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes.Value;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    private static string FormatDuration(long? durationMs)
    {
        if (!durationMs.HasValue || durationMs.Value <= 0) return "0s";
        var ts = TimeSpan.FromMilliseconds(durationMs.Value);
        return ts.TotalSeconds < 60 ? $"{ts.TotalSeconds:0.##}s" : $"{ts.Minutes}m {ts.Seconds}s";
    }

    private async Task<bool> SendTelegramAsync(
        HttpClient client,
        NotificationProfile profile,
        JobExecution execution,
        bool isTest,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.BotToken) || string.IsNullOrWhiteSpace(profile.ChatId))
        {
            throw new InvalidOperationException("Telegram bot token and chat ID are required.");
        }

        var isSuccess = execution.Status == "completed";
        var statusIcon = isSuccess ? "✅ SUCCESS" : "❌ FAILED";
        var title = isTest ? "🧪 [TEST] DBA Control Center Alert" : $"📢 DBA Backup Alert: {statusIcon}";

        var text = new StringBuilder();
        text.AppendLine($"*{title}*");
        text.AppendLine($"• *Job:* `{execution.JobName}`");
        text.AppendLine($"• *Command:* `{execution.CommandType}`");
        text.AppendLine($"• *Status:* `{execution.Status.ToUpperInvariant()}` (Exit: {execution.ExitCode?.ToString() ?? "N/A"})");
        text.AppendLine($"• *Duration:* {FormatDuration(execution.DurationMs)}");
        if (execution.OutputFile != null)
        {
            text.AppendLine($"• *Artifact:* `{execution.OutputFile}` ({FormatBytes(execution.OutputSize)})");
        }
        text.AppendLine($"• *Timestamp:* {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");

        var payload = new
        {
            chat_id = profile.ChatId,
            text = text.ToString(),
            parse_mode = "Markdown"
        };

        var url = $"https://api.telegram.org/bot{profile.BotToken}/sendMessage";
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var res = await client.PostAsync(url, content, ct);
        res.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<bool> SendSlackAsync(
        HttpClient client,
        NotificationProfile profile,
        JobExecution execution,
        bool isTest,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.WebhookUrl))
        {
            throw new InvalidOperationException("Slack webhook URL is required.");
        }

        var isSuccess = execution.Status == "completed";
        var statusColor = isSuccess ? "#22c55e" : "#ef4444";
        var title = isTest ? "🧪 [TEST] DBA Control Center Alert" : $"DBA Backup Alert: {execution.JobName} ({execution.Status.ToUpperInvariant()})";

        var payload = new
        {
            attachments = new[]
            {
                new
                {
                    fallback = title,
                    color = statusColor,
                    title,
                    fields = new[]
                    {
                        new { title = "Job Name", value = execution.JobName, @short = true },
                        new { title = "Command", value = execution.CommandType, @short = true },
                        new { title = "Status", value = execution.Status, @short = true },
                        new { title = "Exit Code", value = execution.ExitCode?.ToString() ?? "N/A", @short = true },
                        new { title = "Duration", value = FormatDuration(execution.DurationMs), @short = true },
                        new { title = "Artifact Size", value = FormatBytes(execution.OutputSize), @short = true },
                        new { title = "Artifact", value = execution.OutputFile ?? "N/A", @short = false }
                    },
                    ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var res = await client.PostAsync(profile.WebhookUrl, content, ct);
        res.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<bool> SendDiscordAsync(
        HttpClient client,
        NotificationProfile profile,
        JobExecution execution,
        bool isTest,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.WebhookUrl))
        {
            throw new InvalidOperationException("Discord webhook URL is required.");
        }

        var isSuccess = execution.Status == "completed";
        var color = isSuccess ? 0x22c55e : 0xef4444;
        var title = isTest ? "🧪 [TEST] DBA Control Center Alert" : $"DBA Backup Alert: {execution.JobName}";

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title,
                    color,
                    fields = new[]
                    {
                        new { name = "Job", value = execution.JobName, inline = true },
                        new { name = "Command", value = execution.CommandType, inline = true },
                        new { name = "Status", value = execution.Status.ToUpperInvariant(), inline = true },
                        new { name = "Exit Code", value = execution.ExitCode?.ToString() ?? "N/A", inline = true },
                        new { name = "Duration", value = FormatDuration(execution.DurationMs), inline = true },
                        new { name = "Size", value = FormatBytes(execution.OutputSize), inline = true },
                        new { name = "File", value = execution.OutputFile ?? "None", inline = false }
                    },
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            }
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var res = await client.PostAsync(profile.WebhookUrl, content, ct);
        res.EnsureSuccessStatusCode();
        return true;
    }

    private async Task<bool> SendWebhookAsync(
        HttpClient client,
        NotificationProfile profile,
        JobExecution execution,
        bool isTest,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.WebhookUrl))
        {
            throw new InvalidOperationException("Webhook URL is required.");
        }

        var payload = new
        {
            @event = isTest ? "test_alert" : "job_execution",
            isTest,
            executionId = execution.Id,
            jobId = execution.JobId,
            jobName = execution.JobName,
            commandType = execution.CommandType,
            status = execution.Status,
            exitCode = execution.ExitCode,
            trigger = execution.Trigger,
            startedAt = execution.StartedAt,
            finishedAt = execution.FinishedAt,
            durationMs = execution.DurationMs,
            outputFile = execution.OutputFile,
            outputSize = execution.OutputSize,
            isDryRun = execution.IsDryRun,
            timestamp = DateTime.UtcNow
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var res = await client.PostAsync(profile.WebhookUrl, content, ct);
        res.EnsureSuccessStatusCode();
        return true;
    }

    private Task<bool> SendEmailAsync(
        NotificationProfile profile,
        JobExecution execution,
        bool isTest,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.SmtpHost) || string.IsNullOrWhiteSpace(profile.EmailTo))
        {
            throw new InvalidOperationException("SMTP host and recipient email (EmailTo) are required.");
        }

        var fromAddress = !string.IsNullOrWhiteSpace(profile.EmailFrom) ? profile.EmailFrom : "noreply@dbatool.local";
        var isSuccess = execution.Status == "completed";
        var subject = isTest
            ? "[TEST] nfrastack DBA Control Center Notification"
            : $"[DBA Alert] [{execution.Status.ToUpperInvariant()}] Job '{execution.JobName}'";

        var body = $@"nfrastack DBA Control Center Notification
=========================================
Job Name:     {execution.JobName}
Command:      {execution.CommandType}
Status:       {execution.Status.ToUpperInvariant()}
Exit Code:    {execution.ExitCode?.ToString() ?? "N/A"}
Duration:     {FormatDuration(execution.DurationMs)}
Output File:  {execution.OutputFile ?? "N/A"}
Output Size:  {FormatBytes(execution.OutputSize)}
Trigger:      {execution.Trigger}
Timestamp:    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
{(isTest ? "\n* This is a test notification generated by DBA Control Center." : "")}
";

        using var message = new MailMessage(fromAddress, profile.EmailTo, subject, body);
        using var smtp = new SmtpClient(profile.SmtpHost, profile.SmtpPort > 0 ? profile.SmtpPort : 587)
        {
            EnableSsl = profile.SmtpUseSsl,
            Timeout = 15000
        };

        if (!string.IsNullOrWhiteSpace(profile.SmtpUsername))
        {
            smtp.Credentials = new NetworkCredential(profile.SmtpUsername, profile.SmtpPassword ?? "");
        }

        smtp.Send(message);
        return Task.FromResult(true);
    }
}
