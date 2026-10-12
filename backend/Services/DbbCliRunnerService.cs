using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CliWrap;
using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Hubs;
using DbaTool.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Services;

public sealed class DbbCliRunnerService(
    IServiceScopeFactory scopeFactory,
    IHubContext<DbaHub> hubContext,
    IDbbConfigGeneratorService configGenerator,
    IDbaPathProvider pathResolver,
    ILogger<DbbCliRunnerService> logger) : IDbbCliRunnerService
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeProcesses = new();

    private static readonly HashSet<string> AllowedSubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "dump", "backup", "restore", "list", "verify", "prune", "archive",
        "maintain", "scheduler", "schedule", "version", "license", "stats", "help"
    };

    public string DataDir => pathResolver.DataDir;

    public string ResolveBinaryPath() => pathResolver.ResolveBinaryPath();

    public bool CancelExecution(int executionId)
    {
        if (_activeProcesses.TryRemove(executionId, out var cts))
        {
            try
            {
                cts.Cancel();
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error cancelling execution {ExecutionId}", executionId);
            }
        }
        return false;
    }

    public async Task<JobExecution> RunJobBackupAsync(int jobId, bool dryRun, string trigger, CancellationToken ct = default)
    {
        await configGenerator.RegenerateYamlAsync(ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.BackupJobs
            .Include(j => j.ConnectionProfile)
            .Include(j => j.StorageProfile)
            .Include(j => j.EncryptionProfile)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new EntityNotFoundException("BackupJob", jobId);

        job.LastRunAt = DateTime.UtcNow;
        job.LastStatus = "running";
        await db.SaveChangesAsync(ct);

        await hubContext.Clients.All.SendAsync("SchedulerUpdated", new
        {
            jobId = job.Id,
            jobName = job.Name,
            nextRunAt = job.NextRunAt,
            lastRunAt = job.LastRunAt,
            lastStatus = job.LastStatus
        }, ct);

        if (job.StandaloneMaintenanceOnly)
        {
            var maintExec = await RunJobMaintenanceAsync(jobId, trigger, ct);
            using var maintScope = scopeFactory.CreateScope();
            var maintDb = maintScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var maintJob = await maintDb.BackupJobs.FindAsync([job.Id], ct);
            if (maintJob != null)
            {
                maintJob.LastRunAt = maintExec.FinishedAt ?? DateTime.UtcNow;
                maintJob.LastStatus = maintExec.Status == "completed" ? "completed" : "failed";
                await maintDb.SaveChangesAsync(ct);

                await hubContext.Clients.All.SendAsync("SchedulerUpdated", new
                {
                    jobId = maintJob.Id,
                    jobName = maintJob.Name,
                    nextRunAt = maintJob.NextRunAt,
                    lastRunAt = maintJob.LastRunAt,
                    lastStatus = maintJob.LastStatus
                }, ct);
            }
            return maintExec;
        }

        var args = new List<string>
        {
            "-c", configGenerator.ConfigPath,
            "--log-format", "json",
            "--log-level", string.IsNullOrWhiteSpace(job.LogLevel) ? "info" : job.LogLevel
        };

        if (job.ProgressEnabled)
        {
            args.Add("--progress");
        }
        if (dryRun)
        {
            args.Add("--dry-run");
        }

        args.Add("dump");
        args.Add(job.Name);

        var execution = await ExecuteCliInternalAsync(
            jobId: job.Id,
            jobName: job.Name,
            commandType: "dump",
            trigger: trigger,
            args: args,
            isDryRun: dryRun
        );

        using var updateScope = scopeFactory.CreateScope();
        var updateDb = updateScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedJob = await updateDb.BackupJobs.FindAsync([job.Id], ct);
        if (updatedJob != null)
        {
            updatedJob.LastRunAt = execution.FinishedAt ?? DateTime.UtcNow;
            updatedJob.LastStatus = execution.Status == "completed" ? "completed" : "failed";
            await updateDb.SaveChangesAsync(ct);

            await hubContext.Clients.All.SendAsync("SchedulerUpdated", new
            {
                jobId = updatedJob.Id,
                jobName = updatedJob.Name,
                nextRunAt = updatedJob.NextRunAt,
                lastRunAt = updatedJob.LastRunAt,
                lastStatus = updatedJob.LastStatus
            }, ct);
        }

        return execution;
    }

    public async Task<JobExecution> RunJobMaintenanceAsync(int jobId, string trigger, CancellationToken ct = default)
    {
        await configGenerator.RegenerateYamlAsync(ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.BackupJobs
            .Include(j => j.ConnectionProfile)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new EntityNotFoundException("BackupJob", jobId);

        job.LastRunAt = DateTime.UtcNow;
        job.LastStatus = "running";
        await db.SaveChangesAsync(ct);

        await hubContext.Clients.All.SendAsync("SchedulerUpdated", new
        {
            jobId = job.Id,
            jobName = job.Name,
            nextRunAt = job.NextRunAt,
            lastRunAt = job.LastRunAt,
            lastStatus = job.LastStatus
        }, ct);

        var args = new List<string>
        {
            "-c", configGenerator.ConfigPath,
            "--log-format", "json",
            "maintain"
        };

        bool anyFlag = false;
        if (job.MaintCheckTables) { args.Add("--check-tables"); anyFlag = true; }
        if (job.MaintOptimize) { args.Add("--optimize"); anyFlag = true; }
        if (job.MaintVacuum) { args.Add("--vacuum"); anyFlag = true; }
        if (job.MaintReindex) { args.Add("--reindex"); anyFlag = true; }
        if (job.MaintAnalyze) { args.Add("--analyze"); anyFlag = true; }
        if (job.MaintCompact) { args.Add("--compact"); anyFlag = true; }
        if (job.MaintMemoryPurge) { args.Add("--memory-purge"); anyFlag = true; }

        if (!anyFlag)
        {
            var connType = (job.ConnectionProfile?.Type ?? "sqlite").ToLowerInvariant();
            if (connType is "sqlite" or "sqlite3" or "postgres" or "pgsql" or "postgresql")
            {
                args.Add("--analyze");
            }
            else if (connType is "mysql" or "mariadb" or "mssql")
            {
                args.Add("--check-tables");
            }
            else if (connType is "mongo" or "mongodb")
            {
                args.Add("--compact");
            }
            else if (connType is "redis")
            {
                args.Add("--memory-purge");
            }
            else
            {
                args.Add("--check-tables");
            }
        }

        args.Add(job.Name);

        var execution = await ExecuteCliInternalAsync(
            jobId: job.Id,
            jobName: job.Name,
            commandType: "maintain",
            trigger: trigger,
            args: args,
            isDryRun: false
        );

        using var updateScope = scopeFactory.CreateScope();
        var updateDb = updateScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedJob = await updateDb.BackupJobs.FindAsync([job.Id], ct);
        if (updatedJob != null)
        {
            updatedJob.LastRunAt = execution.FinishedAt ?? DateTime.UtcNow;
            updatedJob.LastStatus = execution.Status == "completed" ? "completed" : "failed";
            await updateDb.SaveChangesAsync(ct);

            await hubContext.Clients.All.SendAsync("SchedulerUpdated", new
            {
                jobId = updatedJob.Id,
                jobName = updatedJob.Name,
                nextRunAt = updatedJob.NextRunAt,
                lastRunAt = updatedJob.LastRunAt,
                lastStatus = updatedJob.LastStatus
            }, ct);
        }

        return execution;
    }

    public async Task<JobExecution> VerifyBackupAsync(VerifyBackupRequest req, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = await ResolveStorageProfileAsync(db, req.StorageProfileId);

        var args = new List<string>
        {
            "--log-format", "json",
            "verify",
            "--file", req.Filename,
            "--storage-path", storage.Path
        };

        if (!string.Equals(storage.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
        {
            await configGenerator.RegenerateYamlAsync(ct);
            args.InsertRange(0, ["-c", configGenerator.ConfigPath]);
            args.Add("--storage-profile");
            args.Add(storage.Name);
        }

        if (!string.IsNullOrWhiteSpace(req.Passphrase))
        {
            args.Add("--age-passphrase");
            args.Add(req.Passphrase);
        }
        else if (req.Filename.EndsWith(".age", StringComparison.OrdinalIgnoreCase))
        {
            var ageEnc = await db.EncryptionProfiles.FirstOrDefaultAsync(e => e.Type == "age" && !string.IsNullOrEmpty(e.Passphrase), ct);
            if (ageEnc?.Passphrase != null)
            {
                args.Add("--age-passphrase");
                args.Add(ageEnc.Passphrase);
            }
        }

        if (!string.IsNullOrWhiteSpace(req.Identity))
        {
            args.Add("--identity");
            args.Add(req.Identity);
        }

        return await ExecuteCliInternalAsync(
            jobId: null,
            jobName: $"verify:{req.Filename}",
            commandType: "verify",
            trigger: "manual",
            args: args,
            isDryRun: false
        );
    }

    public async Task<JobExecution> RestoreBackupAsync(RestoreBackupRequest req, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = await ResolveStorageProfileAsync(db, req.StorageProfileId);

        ConnectionProfile? conn = null;
        if (req.ConnectionProfileId.HasValue)
        {
            conn = await db.ConnectionProfiles.FindAsync([req.ConnectionProfileId.Value], ct);
        }
        conn ??= await db.ConnectionProfiles.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);

        var args = new List<string>
        {
            "--log-format", "json",
            "restore",
            "--file", req.Filename,
            "--storage-path", storage.Path,
            "--non-interactive"
        };

        if (!string.Equals(storage.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
        {
            await configGenerator.RegenerateYamlAsync(ct);
            args.InsertRange(0, ["-c", configGenerator.ConfigPath]);
            args.Add("--storage-profile");
            args.Add(storage.Name);
        }

        if (conn != null)
        {
            args.Add("--type");
            args.Add(conn.Type);
            args.Add("--host");
            args.Add(conn.Host);

            if (conn.Port > 0)
            {
                args.Add("--port");
                args.Add(conn.Port.ToString());
            }
            if (!string.IsNullOrWhiteSpace(conn.Username))
            {
                args.Add("--user");
                args.Add(conn.Username);
            }
            if (!string.IsNullOrWhiteSpace(conn.Password))
            {
                args.Add("--pass");
                args.Add(conn.Password);
            }

            var isSqlite = string.Equals(conn.Type, "sqlite", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(conn.Type, "sqlite3", StringComparison.OrdinalIgnoreCase);
            if (isSqlite)
            {
                // For SQLite, dbb restore requires --name to be the database file path
                var targetPath = (!string.IsNullOrWhiteSpace(req.TargetDatabase)
                                  && (req.TargetDatabase.Contains('/') || req.TargetDatabase.EndsWith(".db", StringComparison.OrdinalIgnoreCase)))
                    ? req.TargetDatabase
                    : conn.Host;
                args.Add("--name");
                args.Add(targetPath);
            }
            else if (!string.IsNullOrWhiteSpace(req.TargetDatabase))
            {
                args.Add("--name");
                args.Add(req.TargetDatabase);
            }
        }
        else if (!string.IsNullOrWhiteSpace(req.TargetDatabase))
        {
            args.Add("--name");
            args.Add(req.TargetDatabase);
        }

        if (!req.CreateDb)
        {
            args.Add("--create-db=false");
        }

        if (!string.IsNullOrWhiteSpace(req.Passphrase))
        {
            args.Add("--age-passphrase");
            args.Add(req.Passphrase);
        }
        else if (req.Filename.EndsWith(".age", StringComparison.OrdinalIgnoreCase))
        {
            var ageEnc = await db.EncryptionProfiles.FirstOrDefaultAsync(e => e.Type == "age" && !string.IsNullOrEmpty(e.Passphrase), ct);
            if (ageEnc?.Passphrase != null)
            {
                args.Add("--age-passphrase");
                args.Add(ageEnc.Passphrase);
            }
        }

        if (!string.IsNullOrWhiteSpace(req.Identity))
        {
            args.Add("--identity");
            args.Add(req.Identity);
        }

        if (!string.IsNullOrWhiteSpace(req.Base))
        {
            args.Add("--base");
            args.Add(req.Base);
        }
        if (!string.IsNullOrWhiteSpace(req.Compress))
        {
            args.Add("--compress");
            args.Add(req.Compress);
        }
        if (!string.IsNullOrWhiteSpace(req.Encryption))
        {
            args.Add("--encryption");
            args.Add(req.Encryption);
        }
        if (!string.IsNullOrWhiteSpace(req.Profile))
        {
            if (!args.Contains("-c"))
            {
                await configGenerator.RegenerateYamlAsync(ct);
                args.InsertRange(0, ["-c", configGenerator.ConfigPath]);
            }
            args.Add("--profile");
            args.Add(req.Profile);
        }
        if (!string.IsNullOrWhiteSpace(req.AuthSource))
        {
            args.Add("--auth-source");
            args.Add(req.AuthSource);
        }
        else if (!string.IsNullOrWhiteSpace(conn?.AuthSource))
        {
            args.Add("--auth-source");
            args.Add(conn.AuthSource);
        }

        if (req.DryRun)
        {
            args.Add("--dry-run");
        }

        return await ExecuteCliInternalAsync(
            jobId: null,
            jobName: $"restore:{req.Filename}",
            commandType: "restore",
            trigger: "manual",
            args: args,
            isDryRun: req.DryRun
        );
    }

    public async Task<JobExecution> PruneBackupsAsync(PruneBackupsRequest req, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = await ResolveStorageProfileAsync(db, req.StorageProfileId);

        var args = new List<string>
        {
            "--log-format", "json",
            "prune",
            "--storage-path", storage.Path
        };

        if (!string.Equals(storage.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
        {
            await configGenerator.RegenerateYamlAsync(ct);
            args.InsertRange(0, ["-c", configGenerator.ConfigPath]);
            args.Add("--storage-profile");
            args.Add(storage.Name);
        }

        var last = req.Last ?? (string.IsNullOrWhiteSpace(req.Within) ? 5 : 0);
        if (last > 0)
        {
            args.Add("--last");
            args.Add(last.ToString());
        }

        var normalizedWithin = DbbConfigGeneratorService.NormalizeWithinToMinutes(req.Within);
        if (!string.IsNullOrWhiteSpace(normalizedWithin))
        {
            args.Add("--within");
            args.Add(normalizedWithin);
        }

        if (req.DryRun)
        {
            args.Add("--dry-run");
        }

        return await ExecuteCliInternalAsync(
            jobId: null,
            jobName: $"prune:{storage.Name}",
            commandType: "prune",
            trigger: "manual",
            args: args,
            isDryRun: req.DryRun
        );
    }

    public async Task<JobExecution> ArchiveBackupsAsync(ArchiveBackupsRequest req, CancellationToken ct = default)
    {
        await configGenerator.RegenerateYamlAsync(ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var srcStorage = await ResolveStorageProfileAsync(db, req.SrcStorageProfileId);
        StorageProfile? dstStorage = null;
        if (req.DstStorageProfileId.HasValue)
        {
            dstStorage = await db.StorageProfiles.FindAsync([req.DstStorageProfileId.Value], ct);
        }

        var args = new List<string>
        {
            "-c", configGenerator.ConfigPath,
            "--log-format", "json",
            "archive"
        };

        if (!string.IsNullOrWhiteSpace(req.SrcPath))
        {
            args.Add("--src-storage-path");
            args.Add(req.SrcPath);
        }
        else if (string.Equals(srcStorage.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--src-storage-path");
            args.Add(srcStorage.Path);
        }
        else
        {
            args.Add("--src-storage-profile");
            args.Add(srcStorage.Name);
        }

        if (!string.IsNullOrWhiteSpace(req.DstPath))
        {
            args.Add("--dst-storage-path");
            args.Add(req.DstPath);
            Directory.CreateDirectory(req.DstPath);
        }
        else if (dstStorage != null)
        {
            if (string.Equals(dstStorage.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
            {
                args.Add("--dst-storage-path");
                args.Add(dstStorage.Path);
                Directory.CreateDirectory(dstStorage.Path);
            }
            else
            {
                args.Add("--dst-storage-profile");
                args.Add(dstStorage.Name);
            }
        }
        else
        {
            var defaultArchivePath = Path.Combine(DataDir, "archive");
            Directory.CreateDirectory(defaultArchivePath);
            args.Add("--dst-storage-path");
            args.Add(defaultArchivePath);
        }

        if (req.Last is > 0)
        {
            args.Add("--last");
            args.Add(req.Last.Value.ToString());
        }

        var normalizedWithin = DbbConfigGeneratorService.NormalizeWithinToMinutes(req.Within) ?? req.Within?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedWithin))
        {
            args.Add("--within");
            args.Add(normalizedWithin);
        }

        if (req.DryRun)
        {
            args.Add("--dry-run");
        }

        return await ExecuteCliInternalAsync(
            jobId: null,
            jobName: $"archive:{srcStorage.Name}",
            commandType: "archive",
            trigger: "manual",
            args: args,
            isDryRun: req.DryRun
        );
    }

    public async Task<JobExecution> ExecuteCustomCliAsync(CustomCliRequest req, CancellationToken ct = default)
    {
        var sub = (req.Subcommand ?? "").Trim();
        if (!AllowedSubcommands.Contains(sub))
        {
            throw new DomainValidationException($"Unsupported dbb subcommand '{sub}'. Allowed: {string.Join(", ", AllowedSubcommands)}");
        }

        await configGenerator.RegenerateYamlAsync(ct);

        var args = new List<string>();
        var userArgs = req.Args ?? [];

        // Add config path if requested or if running dump/scheduler/maintain with a job name
        if (!string.IsNullOrWhiteSpace(req.ConfigPath))
        {
            args.Add("-c");
            args.Add(req.ConfigPath);
        }
        else if (!userArgs.Contains("-c") && !userArgs.Contains("--config")
                 && sub.Equals("dump", StringComparison.OrdinalIgnoreCase)
                 && !userArgs.Contains("--type"))
        {
            args.Add("-c");
            args.Add(configGenerator.ConfigPath);
        }
        else if (!userArgs.Contains("-c") && !userArgs.Contains("--config")
                 && (sub.Equals("scheduler", StringComparison.OrdinalIgnoreCase) || sub.Equals("schedule", StringComparison.OrdinalIgnoreCase)))
        {
            args.Add("-c");
            args.Add(configGenerator.ConfigPath);
        }

        if (!string.IsNullOrWhiteSpace(req.LogFormat) && !userArgs.Contains("--log-format"))
        {
            args.Add("--log-format");
            args.Add(req.LogFormat);
        }
        if (!string.IsNullOrWhiteSpace(req.LogLevel) && !userArgs.Contains("--log-level"))
        {
            args.Add("--log-level");
            args.Add(req.LogLevel);
        }
        if (req.Progress && !userArgs.Contains("--progress") && !userArgs.Contains("--no-progress")
            && (sub.Equals("dump", StringComparison.OrdinalIgnoreCase) || sub.Equals("backup", StringComparison.OrdinalIgnoreCase)))
        {
            args.Add("--progress");
        }
        if (req.DryRun && !userArgs.Contains("--dry-run"))
        {
            args.Add("--dry-run");
        }

        args.Add(sub);
        args.AddRange(userArgs);

        // Ensure restore in CLI studio never blocks waiting for interactive TTY stdin unless already specified
        if (sub.Equals("restore", StringComparison.OrdinalIgnoreCase) && !userArgs.Contains("--non-interactive"))
        {
            args.Add("--non-interactive");
        }
        // Ensure scheduler in CLI studio runs once unless --once is already passed so it doesn't hang forever unless cancelled
        if ((sub.Equals("scheduler", StringComparison.OrdinalIgnoreCase) || sub.Equals("schedule", StringComparison.OrdinalIgnoreCase))
            && !userArgs.Contains("--once"))
        {
            args.Add("--once");
        }
        // Default --storage-path for list/prune/verify if neither --storage-path nor -c is given
        if ((sub.Equals("list", StringComparison.OrdinalIgnoreCase) || sub.Equals("prune", StringComparison.OrdinalIgnoreCase) || sub.Equals("verify", StringComparison.OrdinalIgnoreCase))
            && !userArgs.Contains("--storage-path") && !args.Contains("-c"))
        {
            args.Add("--storage-path");
            args.Add(Path.Combine(DataDir, "backups"));
        }

        return await ExecuteCliInternalAsync(
            jobId: null,
            jobName: $"cli:{sub}",
            commandType: sub.ToLowerInvariant(),
            trigger: "cli",
            args: args,
            isDryRun: req.DryRun || userArgs.Contains("--dry-run")
        );
    }

    public async Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken ct = default)
    {
        var binaryPath = ResolveBinaryPath();
        var binaryExists = File.Exists(binaryPath);
        var versionInfo = new DbbVersionInfoDto();
        var maintOpsOutput = string.Empty;

        if (binaryExists)
        {
            try
            {
                var stdOut = new StringBuilder();
                await Cli.Wrap(binaryPath)
                    .WithArguments(["version", "--format", "json"])
                    .WithWorkingDirectory(DataDir)
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
                    .ExecuteAsync(ct);

                var jsonText = stdOut.ToString().Trim();
                if (jsonText.StartsWith('{'))
                {
                    var parsed = JsonSerializer.Deserialize<DbbVersionInfoDto>(jsonText);
                    if (parsed != null)
                    {
                        versionInfo = parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to run dbb version --format json");
            }

            try
            {
                var stdOut = new StringBuilder();
                await Cli.Wrap(binaryPath)
                    .WithArguments(["maintain", "--list-ops"])
                    .WithWorkingDirectory(DataDir)
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
                    .ExecuteAsync(ct);

                maintOpsOutput = stdOut.ToString().Trim();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to run dbb maintain --list-ops");
            }
        }

        return new SystemInfoDto
        {
            BinaryPath = binaryPath,
            BinaryAvailable = binaryExists,
            VersionInfo = versionInfo,
            MaintenanceOpsOutput = maintOpsOutput,
            SupportedEngines = GetSupportedEngines(),
            DataDir = DataDir,
            ConfigPath = configGenerator.ConfigPath,
            DatabasePath = pathResolver.SqliteDbPath,
            SampleDatabasePath = string.Empty,
            HangfireDashboardUrl = "/hangfire"
        };
    }

    public async Task<object> CheckVersionAsync(CancellationToken ct = default)
    {
        var binaryPath = ResolveBinaryPath();
        if (!File.Exists(binaryPath))
        {
            return new { available = false, error = "dbb binary not found." };
        }

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        var result = await Cli.Wrap(binaryPath)
            .WithArguments(["version", "check", "--format", "json"])
            .WithWorkingDirectory(DataDir)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
            .ExecuteAsync(ct);

        var raw = stdOut.ToString().Trim();
        if (raw.StartsWith('{'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<JsonElement>(raw);
                return new
                {
                    available = true,
                    exitCode = result.ExitCode,
                    release = parsed
                };
            }
            catch
            {
                // Fall through to raw output
            }
        }

        return new
        {
            available = result.ExitCode == 0,
            exitCode = result.ExitCode,
            rawOutput = string.IsNullOrWhiteSpace(raw) ? stdErr.ToString().Trim() : raw
        };
    }

    public async Task<object> GetLicenseInfoAsync(CancellationToken ct = default)
    {
        var binaryPath = ResolveBinaryPath();
        var sysInfo = await GetSystemInfoAsync(ct);
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        if (File.Exists(binaryPath))
        {
            await Cli.Wrap(binaryPath)
                .WithArguments(["license", "--show-customer"])
                .WithWorkingDirectory(DataDir)
                .WithEnvironmentVariables(env => env.Set("DBBACKUP_HOME", pathResolver.StateDir))
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
                .ExecuteAsync(ct);
        }

        var features = new[]
        {
            "notifications", "incremental", "encryption", "storage",
            "archive", "retention", "maintenance", "schedule", "restore"
        };

        var featureMatrix = new List<object>();
        foreach (var f in features)
        {
            var fOut = new StringBuilder();
            var fErr = new StringBuilder();
            var exitCode = -1;
            if (File.Exists(binaryPath))
            {
                var res = await Cli.Wrap(binaryPath)
                    .WithArguments(["license", "--check", f])
                    .WithWorkingDirectory(DataDir)
                    .WithEnvironmentVariables(env => env.Set("DBBACKUP_HOME", pathResolver.StateDir))
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(fOut))
                    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(fErr))
                    .ExecuteAsync(ct);
                exitCode = res.ExitCode;
            }
            var msg = (fOut.ToString() + " " + fErr.ToString()).Trim();
            featureMatrix.Add(new
            {
                feature = f,
                licensed = exitCode == 0,
                communityAvailable = f is not "notifications",
                message = string.IsNullOrWhiteSpace(msg) ? (exitCode == 0 ? "Licensed" : "Community mode") : msg
            });
        }

        return new
        {
            edition = sysInfo.VersionInfo.Edition,
            mode = sysInfo.VersionInfo.Mode,
            licenseSummary = sysInfo.VersionInfo.License,
            customerOutput = (stdOut.ToString() + "\n" + stdErr.ToString()).Trim(),
            features = featureMatrix
        };
    }

    public async Task<object> CheckLicenseFeatureAsync(string feature, CancellationToken ct = default)
    {
        var binaryPath = ResolveBinaryPath();
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        var result = await Cli.Wrap(binaryPath)
            .WithArguments(["license", "--check", feature])
            .WithWorkingDirectory(DataDir)
            .WithEnvironmentVariables(env => env.Set("DBBACKUP_HOME", pathResolver.StateDir))
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
            .ExecuteAsync(ct);

        return new
        {
            feature,
            valid = result.ExitCode == 0,
            exitCode = result.ExitCode,
            output = (stdOut.ToString() + " " + stdErr.ToString()).Trim()
        };
    }

    public async Task<object> InstallLicenseAsync(LicenseInstallRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.TokenOrPath))
        {
            throw new DomainValidationException("License token or file path is required.");
        }

        var binaryPath = ResolveBinaryPath();
        var targetPath = req.TokenOrPath.Trim();

        // If user pasted raw license content rather than a file path, save it to StateDir/license.key
        if (!File.Exists(targetPath))
        {
            Directory.CreateDirectory(pathResolver.StateDir);
            var licenseFile = Path.Combine(pathResolver.StateDir, "license.key");
            await File.WriteAllTextAsync(licenseFile, targetPath, Encoding.UTF8, ct);
            targetPath = licenseFile;
        }

        var args = new List<string> { "license", "--install", targetPath };
        if (req.System)
        {
            args.Add("--system");
        }

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        var result = await Cli.Wrap(binaryPath)
            .WithArguments(args)
            .WithWorkingDirectory(DataDir)
            .WithEnvironmentVariables(env => env.Set("DBBACKUP_HOME", pathResolver.StateDir))
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
            .ExecuteAsync(ct);

        return new
        {
            success = result.ExitCode == 0,
            exitCode = result.ExitCode,
            output = (stdOut.ToString() + "\n" + stdErr.ToString()).Trim()
        };
    }

    public async Task<object> GetStatsAsync(CancellationToken ct = default)
    {
        var binaryPath = ResolveBinaryPath();
        var statsDir = Path.Combine(pathResolver.StateDir, "stats");
        Directory.CreateDirectory(statsDir);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        var result = await Cli.Wrap(binaryPath)
            .WithArguments(["stats", "--format", "json"])
            .WithWorkingDirectory(DataDir)
            .WithEnvironmentVariables(env => env.Set("DBBACKUP_HOME", pathResolver.StateDir))
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdOut))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErr))
            .ExecuteAsync(ct);

        var rawOut = stdOut.ToString().Trim();
        var rawErr = stdErr.ToString().Trim();
        object? parsedJson = null;
        if (rawOut.StartsWith('{') || rawOut.StartsWith('['))
        {
            try
            {
                parsedJson = JsonSerializer.Deserialize<JsonElement>(rawOut);
            }
            catch
            {
                // Ignore parse error
            }
        }

        return new
        {
            available = result.ExitCode == 0 && parsedJson != null,
            exitCode = result.ExitCode,
            statsDir,
            data = parsedJson,
            message = string.IsNullOrWhiteSpace(rawOut) ? rawErr : rawOut
        };
    }

    public async Task<object> TestConnectionAsync(ConnectionProfile conn, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var type = conn.Type.ToLowerInvariant();
            if (type is "sqlite" or "sqlite3")
            {
                if (!File.Exists(conn.Host))
                {
                    return new
                    {
                        success = false,
                        latencyMs = sw.ElapsedMilliseconds,
                        message = $"SQLite file not found at '{conn.Host}'",
                        details = "Verify that the file path exists and is readable."
                    };
                }

                var cs = new SqliteConnectionStringBuilder
                {
                    DataSource = conn.Host,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString();

                await using var sqliteConn = new SqliteConnection(cs);
                await sqliteConn.OpenAsync(ct);

                await using var cmd = sqliteConn.CreateCommand();
                cmd.CommandText = "SELECT sqlite_version();";
                var ver = Convert.ToString(await cmd.ExecuteScalarAsync(ct));

                await using var tblCmd = sqliteConn.CreateCommand();
                tblCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                var tableCount = Convert.ToInt32(await tblCmd.ExecuteScalarAsync(ct));

                sw.Stop();
                return new
                {
                    success = true,
                    latencyMs = sw.ElapsedMilliseconds,
                    message = $"Connected to SQLite {ver} ({tableCount} user tables)",
                    details = $"File: {conn.Host} ({new FileInfo(conn.Host).Length:N0} bytes)"
                };
            }

            var port = conn.Port > 0 ? conn.Port : GetDefaultPort(type);
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(conn.TimeoutSeconds, 2, 10)));
            await tcp.ConnectAsync(conn.Host, port, cts.Token);
            sw.Stop();

            return new
            {
                success = true,
                latencyMs = sw.ElapsedMilliseconds,
                message = $"TCP connection to {conn.Host}:{port} ({conn.Type}) succeeded in {sw.ElapsedMilliseconds}ms",
                details = $"Endpoint {conn.Host}:{port} is reachable."
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new
            {
                success = false,
                latencyMs = sw.ElapsedMilliseconds,
                message = $"Connection failed: {ex.Message}",
                details = $"{conn.Type}://{conn.Host}:{conn.Port}"
            };
        }
    }

    private async Task<JobExecution> ExecuteCliInternalAsync(
        int? jobId,
        string jobName,
        string commandType,
        string trigger,
        List<string> args,
        bool isDryRun)
    {
        var binaryPath = ResolveBinaryPath();
        var cliDisplay = $"dbb {string.Join(" ", args.Select(QuoteArgIfNeeded))}";
        var startedAt = DateTime.UtcNow;

        var execution = new JobExecution
        {
            JobId = jobId,
            JobName = jobName,
            CommandType = commandType,
            Trigger = trigger,
            CliCommand = cliDisplay,
            Status = "running",
            StartedAt = startedAt,
            IsDryRun = isDryRun,
            LogOutput = string.Empty
        };

        using (var initScope = scopeFactory.CreateScope())
        {
            var db = initScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.JobExecutions.Add(execution);
            await db.SaveChangesAsync();
        }

        var executionId = execution.Id;
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        _activeProcesses[executionId] = cts;

        await hubContext.Clients.All.SendAsync("ExecutionStarted", new
        {
            executionId,
            jobId,
            jobName,
            commandType,
            cliCommand = cliDisplay,
            startedAt,
            trigger,
            isDryRun
        });

        var logBuffer = new StringBuilder();
        var logLock = new object();
        string? detectedOutputFile = null;
        long? detectedOutputSize = null;
        string currentStage = string.Equals(commandType, "maintain", StringComparison.OrdinalIgnoreCase) ? "MAINTAIN" : "INIT";
        ProcessErrorDiagnostic? activeDiagnostic = null;
        bool hasDetectedError = false;
        long currentBytes = 0;
        string currentRate = string.Empty;
        string currentElapsed = string.Empty;
        string? currentTable = null;

        async Task HandleLineAsync(string stream, string line)
        {
            if (line == null) return;

            JsonElement? parsedJson = null;
            var trimmed = line.Trim();
            string? lineStatus = null;
            if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    parsedJson = doc.RootElement.Clone();

                    var root = parsedJson.Value;
                    lineStatus = root.TryGetProperty("status", out var stProp) ? stProp.GetString() : null;
                    var action = root.TryGetProperty("action", out var actProp) ? actProp.GetString() : null;

                    if (lineStatus == "progress")
                    {
                        if (root.TryGetProperty("bytes", out var bProp))
                        {
                            if (bProp.ValueKind == JsonValueKind.Number) currentBytes = bProp.GetInt64();
                            else if (bProp.ValueKind == JsonValueKind.String && long.TryParse(bProp.GetString(), out var bp)) currentBytes = bp;
                        }

                        currentRate = root.TryGetProperty("rate", out var rProp) ? rProp.GetString() ?? "" : "";
                        currentElapsed = root.TryGetProperty("elapsed", out var eProp) ? eProp.GetString() ?? "" : "";
                        currentTable = root.TryGetProperty("table", out var tProp) ? tProp.GetString() : null;
                        var step = root.TryGetProperty("step", out var sProp) ? sProp.GetString() : "";

                        await hubContext.Clients.All.SendAsync("ExecutionProgress", new
                        {
                            executionId,
                            jobId,
                            bytes = currentBytes,
                            rate = currentRate,
                            elapsed = currentElapsed,
                            table = currentTable,
                            step
                        });
                    }
                    else if (lineStatus == "complete" && action == "backup")
                    {
                        if (root.TryGetProperty("file", out var fProp) && fProp.ValueKind == JsonValueKind.String)
                        {
                            detectedOutputFile = Path.GetFileName(fProp.GetString());
                        }
                        if (root.TryGetProperty("size", out var szProp))
                        {
                            if (szProp.ValueKind == JsonValueKind.Number && szProp.TryGetInt64(out var szNum))
                            {
                                detectedOutputSize = szNum;
                            }
                            else if (szProp.ValueKind == JsonValueKind.String && long.TryParse(szProp.GetString(), out var szStr))
                            {
                                detectedOutputSize = szStr;
                            }
                        }
                    }
                }
                catch
                {
                    // Non-JSON or malformed JSON line
                }
            }

            var effectiveStream = stream;
            if (stream == "stderr" && parsedJson.HasValue)
            {
                var level = parsedJson.Value.TryGetProperty("level", out var lvlProp)
                    ? lvlProp.GetString()?.ToLowerInvariant()
                    : null;
                // dbb sends all structured logs to stderr by unix convention.
                // Map info/debug/notice logs to stdout so they are not treated as error streams.
                if (level is "info" or "debug" or "notice")
                {
                    effectiveStream = "stdout";
                }
            }

            lock (logLock)
            {
                logBuffer.AppendLine($"[{effectiveStream.ToUpperInvariant()}] {line}");
            }

            var prevStage = currentStage;
            currentStage = ProcessDiagnosticEngine.DetectStageFromLog(line, parsedJson, currentStage);
            bool stageChanged = currentStage != prevStage;

            // Check for error lines on both stdout and stderr (dbb maintain and other commands print results to stdout)
            bool isExplicitError = line.Contains('✗')
                || line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("does not have permission", StringComparison.OrdinalIgnoreCase)
                || line.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
                || line.Contains("access denied", StringComparison.OrdinalIgnoreCase)
                || (effectiveStream == "stderr" && (line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("error", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("refused", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("denied", StringComparison.OrdinalIgnoreCase)));

            if (isExplicitError)
            {
                hasDetectedError = true;
                lock (logLock)
                {
                    activeDiagnostic = ProcessDiagnosticEngine.DiagnoseFailure(logBuffer.ToString(), currentStage, -1);
                }
            }

            if (stageChanged || lineStatus == "progress" || activeDiagnostic != null)
            {
                var def = ProcessDiagnosticEngine.StageDefinitions.TryGetValue(currentStage, out var stDef)
                    ? stDef
                    : (Title: currentStage, Description: string.Empty);

                await hubContext.Clients.All.SendAsync("ExecutionStateChanged", new ExecutionStateChangedEvent
                {
                    ExecutionId = executionId,
                    JobId = jobId,
                    JobName = jobName,
                    CommandType = commandType,
                    CurrentStage = currentStage,
                    StageTitle = def.Title,
                    StageDescription = def.Description,
                    BytesProcessed = currentBytes,
                    Throughput = currentRate,
                    Elapsed = currentElapsed,
                    CurrentTable = currentTable,
                    IsError = activeDiagnostic != null,
                    Diagnostic = activeDiagnostic
                });
            }

            await hubContext.Clients.All.SendAsync("CliOutput", new
            {
                executionId,
                jobId,
                jobName,
                commandType,
                stream = effectiveStream,
                line,
                parsedJson,
                timestamp = DateTime.UtcNow
            });
        }

        var sw = Stopwatch.StartNew();
        int exitCode = -1;
        string finalStatus = "completed";

        try
        {
            var stateDir = Path.Combine(DataDir, "state");
            var tempDir = Path.Combine(DataDir, "tmp");
            Directory.CreateDirectory(stateDir);
            Directory.CreateDirectory(tempDir);

            var cmd = Cli.Wrap(binaryPath)
                .WithArguments(args)
                .WithWorkingDirectory(DataDir)
                .WithEnvironmentVariables(env => env
                    .Set("DBBACKUP_HOME", stateDir)
                    .Set("TMPDIR", tempDir))
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(line => HandleLineAsync("stdout", line)))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line => HandleLineAsync("stderr", line)));

            var result = await cmd.ExecuteAsync(cts.Token);
            exitCode = result.ExitCode;

            string allLogs;
            lock (logLock)
            {
                allLogs = logBuffer.ToString();
            }

            // Certain commands (such as dbb maintain on DBCC checkdb failure) output '✗' to stdout but exit with 0.
            // If failure markers exist in output, override exitCode and finalStatus to failed.
            bool hasFailureIndicators = hasDetectedError
                || allLogs.Contains('✗')
                || allLogs.Contains("does not have permission", StringComparison.OrdinalIgnoreCase);

            if (exitCode == 0 && hasFailureIndicators)
            {
                exitCode = 1;
                finalStatus = "failed";
            }
            else
            {
                finalStatus = exitCode == 0 ? "completed" : "failed";
            }
        }
        catch (OperationCanceledException)
        {
            finalStatus = "cancelled";
            exitCode = -1;
            await HandleLineAsync("stderr", "Execution cancelled by user.");
        }
        catch (Exception ex)
        {
            finalStatus = "failed";
            exitCode = -1;
            await HandleLineAsync("stderr", $"Process execution error: {ex.Message}");
            logger.LogError(ex, "Error executing dbb command: {CliDisplay}", cliDisplay);
        }
        finally
        {
            sw.Stop();
            _activeProcesses.TryRemove(executionId, out var removedCts);
            removedCts?.Dispose();
        }

        if (detectedOutputFile != null && (!detectedOutputSize.HasValue || detectedOutputSize == 0))
        {
            var candidatePath = Path.Combine(DataDir, "backups", detectedOutputFile);
            if (File.Exists(candidatePath))
            {
                detectedOutputSize = new FileInfo(candidatePath).Length;
            }
        }

        var finishedAt = DateTime.UtcNow;
        var durationMs = sw.ElapsedMilliseconds;
        string finalLogs;
        lock (logLock)
        {
            finalLogs = logBuffer.ToString();
        }

        var finalDiagnostic = finalStatus == "failed"
            ? (activeDiagnostic ?? ProcessDiagnosticEngine.DiagnoseFailure(finalLogs, currentStage, exitCode))
            : null;

        var finalStage = finalStatus == "completed"
            ? "COMPLETED"
            : (finalDiagnostic?.Stage ?? (finalStatus == "cancelled" ? "CANCELLED" : currentStage));

        using (var finishScope = scopeFactory.CreateScope())
        {
            var db = finishScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dbExec = await db.JobExecutions.FindAsync(executionId);
            if (dbExec != null)
            {
                dbExec.Status = finalStatus;
                dbExec.ExitCode = exitCode;
                dbExec.FinishedAt = finishedAt;
                dbExec.DurationMs = durationMs;
                dbExec.OutputFile = detectedOutputFile;
                dbExec.OutputSize = detectedOutputSize;
                dbExec.LogOutput = finalLogs;
                dbExec.CurrentStage = finalStage;
                dbExec.ErrorSummary = finalDiagnostic?.Summary;
                dbExec.ErrorDetails = finalDiagnostic?.TechnicalDetails;
                await db.SaveChangesAsync();
                execution = dbExec;
            }
        }

        await hubContext.Clients.All.SendAsync("ExecutionCompleted", new
        {
            executionId,
            jobId,
            jobName,
            commandType,
            status = finalStatus,
            exitCode,
            durationMs,
            outputFile = detectedOutputFile,
            outputSize = detectedOutputSize,
            finishedAt,
            currentStage = finalStage,
            errorSummary = finalDiagnostic?.Summary,
            diagnostic = finalDiagnostic
        });

        // Dispatch notifications to configured channels
        try
        {
            using var alertScope = scopeFactory.CreateScope();
            var notifier = alertScope.ServiceProvider.GetService<INotificationDispatcherService>();
            if (notifier != null)
            {
                await notifier.DispatchExecutionAlertAsync(execution);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to dispatch notification alerts for execution {ExecutionId}", executionId);
        }

        return execution;
    }

    private async Task<StorageProfile> ResolveStorageProfileAsync(AppDbContext db, int? storageProfileId)
    {
        StorageProfile? storage = null;
        if (storageProfileId.HasValue)
        {
            storage = await db.StorageProfiles.FindAsync(storageProfileId.Value);
        }
        storage ??= await db.StorageProfiles.FirstOrDefaultAsync(s => s.IsDefault)
                    ?? await db.StorageProfiles.FirstOrDefaultAsync()
                    ?? new StorageProfile
                    {
                        Name = "default-local",
                        Backend = "filesystem",
                        Path = pathResolver.BackupsDir
                    };
        return storage;
    }

    private static string QuoteArgIfNeeded(string arg) =>
        arg.Contains(' ') || arg.Contains('*') ? $"\"{arg}\"" : arg;

    private static int GetDefaultPort(string type) => type switch
    {
        "mysql" or "mariadb" => 3306,
        "postgres" or "pgsql" or "postgresql" => 5432,
        "mongo" or "mongodb" => 27017,
        "mssql" or "microsoftsql" => 1433,
        "redis" => 6379,
        "influx" => 8086,
        "couch" or "couchdb" => 5984,
        _ => 0
    };

    public static List<SupportedEngineDto> GetSupportedEngines() =>
    [
        new()
        {
            Type = "mysql",
            Aliases = ["mariadb"],
            Label = "MySQL / MariaDB",
            DefaultPort = 3306,
            Extension = ".sql",
            SupportsListDbs = true,
            SupportsIncremental = true,
            MaintenanceOps = ["check_tables", "optimize", "analyze"]
        },
        new()
        {
            Type = "postgres",
            Aliases = ["pgsql", "postgresql"],
            Label = "PostgreSQL",
            DefaultPort = 5432,
            Extension = ".sql",
            SupportsListDbs = true,
            SupportsIncremental = true,
            MaintenanceOps = ["vacuum", "reindex", "analyze"]
        },
        new()
        {
            Type = "mongo",
            Aliases = ["mongodb"],
            Label = "MongoDB",
            DefaultPort = 27017,
            Extension = ".mongo",
            SupportsListDbs = true,
            SupportsIncremental = true,
            MaintenanceOps = ["compact"]
        },
        new()
        {
            Type = "mssql",
            Aliases = ["microsoftsql"],
            Label = "Microsoft SQL Server",
            DefaultPort = 1433,
            Extension = ".sql",
            SupportsListDbs = true,
            SupportsIncremental = true,
            MaintenanceOps = ["check_tables"]
        },
        new()
        {
            Type = "redis",
            Aliases = [],
            Label = "Redis",
            DefaultPort = 6379,
            Extension = ".redis",
            SupportsListDbs = false,
            SupportsIncremental = true,
            MaintenanceOps = ["memory_purge"]
        },
        new()
        {
            Type = "sqlite",
            Aliases = ["sqlite3"],
            Label = "SQLite 3",
            DefaultPort = 0,
            Extension = ".sql",
            SupportsListDbs = false,
            SupportsIncremental = false,
            MaintenanceOps = ["analyze"]
        },
        new()
        {
            Type = "influx",
            Aliases = [],
            Label = "InfluxDB (v1 / v2)",
            DefaultPort = 8086,
            Extension = ".influx",
            SupportsListDbs = true,
            SupportsIncremental = true,
            MaintenanceOps = []
        },
        new()
        {
            Type = "couch",
            Aliases = ["couchdb"],
            Label = "CouchDB",
            DefaultPort = 5984,
            Extension = ".couch",
            SupportsListDbs = true,
            SupportsIncremental = false,
            MaintenanceOps = []
        }
    ];
}
