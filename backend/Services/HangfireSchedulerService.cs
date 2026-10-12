using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Hubs;
using DbaTool.Api.Models;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Services;

public sealed class HangfireSchedulerService(
    IServiceScopeFactory scopeFactory,
    IRecurringJobManager recurringJobManager,
    IBackgroundJobClient backgroundJobClient,
    IDbbCliRunnerService cliRunner,
    IHubContext<DbaHub> hubContext,
    ILogger<HangfireSchedulerService> logger) : IHangfireSchedulerService
{
    public static string GetRecurringJobId(int jobId) => $"dbb-job-{jobId}";

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteScheduledBackupAsync(int jobId)
    {
        logger.LogInformation("Hangfire executing scheduled backup for job #{JobId}", jobId);
        await cliRunner.RunJobBackupAsync(jobId, dryRun: false, trigger: "scheduled");
        await SyncJobAsync(jobId);
    }

    public async Task SyncAllJobsAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobs = await db.BackupJobs.ToListAsync(ct);

        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in jobs)
        {
            var recurringId = GetRecurringJobId(job.Id);
            var cron = ResolveCronExpression(job);

            if (job.IsEnabled && !string.IsNullOrWhiteSpace(cron))
            {
                try
                {
                    recurringJobManager.AddOrUpdate<IHangfireSchedulerService>(
                        recurringId,
                        svc => svc.ExecuteScheduledBackupAsync(job.Id),
                        cron,
                        new RecurringJobOptions
                        {
                            TimeZone = TimeZoneInfo.Local
                        });
                    activeIds.Add(recurringId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Invalid cron expression '{Cron}' for job {JobName}; skipping Hangfire registration", cron, job.Name);
                }
            }
            else
            {
                recurringJobManager.RemoveIfExists(recurringId);
                job.NextRunAt = null;
            }
        }

        // Refresh NextRunAt from Hangfire storage
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            var recurringJobs = connection.GetRecurringJobs();
            var recurringMap = recurringJobs.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var job in jobs)
            {
                var recurringId = GetRecurringJobId(job.Id);
                if (recurringMap.TryGetValue(recurringId, out var rDto))
                {
                    job.NextRunAt = rDto.NextExecution;
                }
                else if (!activeIds.Contains(recurringId))
                {
                    job.NextRunAt = null;
                }
            }

            // Remove orphan recurring jobs that no longer exist in DB
            foreach (var rJob in recurringJobs)
            {
                if (rJob.Id.StartsWith("dbb-job-", StringComparison.OrdinalIgnoreCase) && !activeIds.Contains(rJob.Id))
                {
                    recurringJobManager.RemoveIfExists(rJob.Id);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect Hangfire recurring jobs");
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SyncJobAsync(int jobId, CancellationToken ct = default)
    {
        await SyncAllJobsAsync(ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.BackupJobs.FindAsync([jobId], ct);
        if (job != null)
        {
            await hubContext.Clients.All.SendAsync("SchedulerUpdated", new
            {
                jobId = job.Id,
                jobName = job.Name,
                nextRunAt = job.NextRunAt,
                lastRunAt = job.LastRunAt,
                lastStatus = job.LastStatus
            }, ct);
        }
    }

    public void RemoveJob(int jobId)
    {
        recurringJobManager.RemoveIfExists(GetRecurringJobId(jobId));
    }

    public async Task<List<SchedulerJobDto>> GetScheduledJobsAsync(CancellationToken ct = default)
    {
        await SyncAllJobsAsync(ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobs = await db.BackupJobs
            .Include(j => j.ConnectionProfile)
            .OrderBy(j => j.Id)
            .ToListAsync(ct);

        var recurringMap = new Dictionary<string, RecurringJobDto>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var connection = JobStorage.Current.GetConnection();
            foreach (var r in connection.GetRecurringJobs())
            {
                recurringMap[r.Id] = r;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not query Hangfire recurring jobs");
        }

        var result = new List<SchedulerJobDto>();
        foreach (var job in jobs)
        {
            var recurringId = GetRecurringJobId(job.Id);
            recurringMap.TryGetValue(recurringId, out var rDto);

            var cron = ResolveCronExpression(job);
            var hasSchedule = !string.IsNullOrWhiteSpace(cron);
            var isRegistered = rDto != null;

            result.Add(new SchedulerJobDto
            {
                JobId = job.Id,
                JobName = job.Name,
                Description = job.Description,
                IsEnabled = job.IsEnabled,
                IsPaused = !job.IsEnabled || (hasSchedule && !isRegistered),
                ScheduleCron = job.ScheduleCron,
                QuartzCron = rDto?.Cron ?? cron,
                ScheduleIntervalMinutes = job.ScheduleIntervalMinutes,
                NextRunAt = rDto?.NextExecution ?? job.NextRunAt,
                PreviousFireTime = rDto?.LastExecution ?? job.LastRunAt,
                LastRunAt = job.LastRunAt,
                LastStatus = job.LastStatus,
                TriggerState = !hasSchedule
                    ? "Manual"
                    : (job.IsEnabled && isRegistered ? "Normal" : "Paused"),
                ConnectionName = job.ConnectionProfile?.Name ?? "unknown",
                EngineType = job.ConnectionProfile?.Type ?? "sqlite",
                Strategy = job.Strategy
            });
        }

        return result;
    }

    public async Task<SchedulerJobDto> PauseJobAsync(int jobId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.BackupJobs.FindAsync([jobId], ct)
                  ?? throw new EntityNotFoundException("ScheduledJob", jobId);

        job.IsEnabled = false;
        job.NextRunAt = null;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        recurringJobManager.RemoveIfExists(GetRecurringJobId(jobId));
        await SyncJobAsync(jobId, ct);

        var all = await GetScheduledJobsAsync(ct);
        return all.First(j => j.JobId == jobId);
    }

    public async Task<SchedulerJobDto> ResumeJobAsync(int jobId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.BackupJobs.FindAsync([jobId], ct)
                  ?? throw new EntityNotFoundException("ScheduledJob", jobId);

        job.IsEnabled = true;
        if (string.IsNullOrWhiteSpace(job.ScheduleCron) && job.ScheduleIntervalMinutes <= 0)
        {
            job.ScheduleCron = "0 */6 * * *";
        }
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await SyncJobAsync(jobId, ct);

        var all = await GetScheduledJobsAsync(ct);
        return all.First(j => j.JobId == jobId);
    }

    public async Task<string> TriggerJobNowAsync(int jobId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.BackupJobs.FindAsync([jobId], ct)
                  ?? throw new EntityNotFoundException("ScheduledJob", jobId);

        var recurringId = GetRecurringJobId(job.Id);
        using var connection = JobStorage.Current.GetConnection();
        var exists = connection.GetRecurringJobs().Any(r => string.Equals(r.Id, recurringId, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            recurringJobManager.Trigger(recurringId);
            return recurringId;
        }

        var bgJobId = backgroundJobClient.Enqueue<IHangfireSchedulerService>(svc => svc.ExecuteScheduledBackupAsync(job.Id));
        return bgJobId;
    }

    public static string? ResolveCronExpression(BackupJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.ScheduleCron))
        {
            var parts = job.ScheduleCron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 5 || parts.Length == 6)
            {
                return string.Join(' ', parts);
            }
        }

        if (job.ScheduleIntervalMinutes > 0)
        {
            var mins = job.ScheduleIntervalMinutes;
            if (mins < 60)
            {
                return $"*/{Math.Max(1, mins)} * * * *";
            }
            var hours = Math.Max(1, mins / 60);
            return $"0 */{hours} * * *";
        }

        return null;
    }
}
