using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Endpoints;

public static class BackupJobEndpoints
{
    public static IEndpointRouteBuilder MapBackupJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Backup Jobs");

        group.MapGet("", GetJobsAsync);
        group.MapGet("/presets", GetPresets);
        group.MapGet("/{id:int}", GetJobByIdAsync);
        group.MapPost("", CreateJobAsync);
        group.MapPut("/{id:int}", UpdateJobAsync);
        group.MapDelete("/{id:int}", DeleteJobAsync);
        group.MapPost("/{id:int}/run", RunJobAsync);
        group.MapPost("/{id:int}/maintain", MaintainJobAsync);
        group.MapPost("/{id:int}/toggle", ToggleJobAsync);

        return app;
    }

    private static async Task<IResult> GetJobsAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        var jobs = await db.BackupJobs
            .AsNoTracking()
            .Include(j => j.ConnectionProfile)
            .Include(j => j.StorageProfile)
            .Include(j => j.EncryptionProfile)
            .OrderBy(j => j.Id)
            .ToListAsync(ct);
        return Results.Ok(jobs);
    }

    private static async Task<IResult> GetJobByIdAsync(
        int id,
        AppDbContext db,
        CancellationToken ct)
    {
        var job = await db.BackupJobs
            .AsNoTracking()
            .Include(j => j.ConnectionProfile)
            .Include(j => j.StorageProfile)
            .Include(j => j.EncryptionProfile)
            .FirstOrDefaultAsync(j => j.Id == id, ct)
            ?? throw new EntityNotFoundException("Job", id);

        return Results.Ok(job);
    }

    private static async Task<IResult> CreateJobAsync(
        BackupJob input,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        input.Id = 0;
        input.Name = SlugHelper.Sanitize(input.Name, "job");
        input.ConnectionProfile = null;
        input.StorageProfile = null;
        input.EncryptionProfile = null;
        input.CreatedAt = DateTime.UtcNow;
        input.UpdatedAt = DateTime.UtcNow;

        db.BackupJobs.Add(input);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);
        await scheduler.SyncJobAsync(input.Id, ct);

        var created = await db.BackupJobs
            .AsNoTracking()
            .Include(j => j.ConnectionProfile)
            .Include(j => j.StorageProfile)
            .Include(j => j.EncryptionProfile)
            .FirstAsync(j => j.Id == input.Id, ct);

        return Results.Created($"/api/jobs/{input.Id}", created);
    }

    private static async Task<IResult> UpdateJobAsync(
        int id,
        BackupJob input,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        var existing = await db.BackupJobs.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Job", id);

        existing.Name = SlugHelper.Sanitize(input.Name, existing.Name);
        existing.Description = input.Description;
        existing.IsEnabled = input.IsEnabled;
        existing.ConnectionProfileId = input.ConnectionProfileId;
        existing.DatabaseNames = string.IsNullOrWhiteSpace(input.DatabaseNames) ? "ALL" : input.DatabaseNames;
        existing.ExcludeDatabases = input.ExcludeDatabases;
        existing.SplitDb = input.SplitDb;
        existing.TablesInclude = input.TablesInclude;
        existing.TablesExclude = input.TablesExclude;
        existing.TablesSchemaOnly = input.TablesSchemaOnly;
        existing.SchemaOnly = input.SchemaOnly;
        existing.MySqlRoutines = input.MySqlRoutines;
        existing.MySqlEvents = input.MySqlEvents;
        existing.MySqlTriggers = input.MySqlTriggers;
        existing.MySqlViews = input.MySqlViews;
        existing.MySqlRawBlobs = input.MySqlRawBlobs;
        existing.PostgresGlobals = input.PostgresGlobals;
        existing.Strategy = string.IsNullOrWhiteSpace(input.Strategy) ? "full" : input.Strategy;
        existing.FilenameTemplate = string.IsNullOrWhiteSpace(input.FilenameTemplate)
            ? "%type%-%db%-%host%-%strategy%-%timestamp%"
            : input.FilenameTemplate;
        existing.CreateLatest = input.CreateLatest;
        existing.FullEvery = input.FullEvery;
        existing.FullAfter = input.FullAfter;
        existing.CompressionType = string.IsNullOrWhiteSpace(input.CompressionType) ? "zstd" : input.CompressionType;
        existing.CompressionLevel = input.CompressionLevel > 0 ? input.CompressionLevel : 3;
        existing.CompressionThreads = input.CompressionThreads > 0 ? input.CompressionThreads : 1;
        existing.CompressionRsyncable = input.CompressionRsyncable;
        existing.Checksum = string.IsNullOrWhiteSpace(input.Checksum) ? "md5" : input.Checksum;
        existing.StorageProfileId = input.StorageProfileId;
        existing.EncryptionProfileId = input.EncryptionProfileId;
        existing.ArchiveEnabled = input.ArchiveEnabled;
        existing.ArchiveLast = input.ArchiveLast;
        existing.ArchiveWithin = input.ArchiveWithin;
        existing.ArchivePath = input.ArchivePath;
        existing.ArchiveStorageProfileId = input.ArchiveStorageProfileId;
        existing.ArchiveRetentionWithin = input.ArchiveRetentionWithin;
        existing.ScheduleCron = input.ScheduleCron;
        existing.ScheduleIntervalMinutes = input.ScheduleIntervalMinutes;
        existing.ScheduleTime = input.ScheduleTime;
        existing.ScheduleBegin = input.ScheduleBegin;
        existing.ScheduleStart = input.ScheduleStart;
        existing.ScheduleRecurring = input.ScheduleRecurring;
        existing.ScheduleDays = input.ScheduleDays;
        existing.ScheduleExcludeDays = input.ScheduleExcludeDays;
        existing.BlackoutBegin = input.BlackoutBegin;
        existing.BlackoutEnd = input.BlackoutEnd;
        existing.BlackoutDays = input.BlackoutDays;
        existing.RetentionLast = input.RetentionLast;
        existing.RetentionWithin = input.RetentionWithin;
        existing.RetentionHourly = input.RetentionHourly;
        existing.RetentionDaily = input.RetentionDaily;
        existing.RetentionWeekly = input.RetentionWeekly;
        existing.RetentionMonthly = input.RetentionMonthly;
        existing.RetentionYearly = input.RetentionYearly;
        existing.MaintCheckTables = input.MaintCheckTables;
        existing.MaintOptimize = input.MaintOptimize;
        existing.MaintVacuum = input.MaintVacuum;
        existing.MaintReindex = input.MaintReindex;
        existing.MaintAnalyze = input.MaintAnalyze;
        existing.MaintCompact = input.MaintCompact;
        existing.MaintMemoryPurge = input.MaintMemoryPurge;
        existing.MaintenanceScheduleCron = input.MaintenanceScheduleCron;
        existing.StandaloneMaintenanceOnly = input.StandaloneMaintenanceOnly;
        existing.PreHooks = input.PreHooks;
        existing.PostHooks = input.PostHooks;
        existing.LogLevel = string.IsNullOrWhiteSpace(input.LogLevel) ? "info" : input.LogLevel;
        existing.LogPath = input.LogPath;
        existing.ProgressEnabled = input.ProgressEnabled;
        existing.TimingsEnabled = input.TimingsEnabled;
        existing.ColourEnabled = input.ColourEnabled;
        existing.SizeFormat = string.IsNullOrWhiteSpace(input.SizeFormat) ? "bytes" : input.SizeFormat;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);
        await scheduler.SyncJobAsync(existing.Id, ct);

        var updated = await db.BackupJobs
            .AsNoTracking()
            .Include(j => j.ConnectionProfile)
            .Include(j => j.StorageProfile)
            .Include(j => j.EncryptionProfile)
            .FirstAsync(j => j.Id == id, ct);

        return Results.Ok(updated);
    }

    private static async Task<IResult> DeleteJobAsync(
        int id,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        var existing = await db.BackupJobs.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Job", id);

        scheduler.RemoveJob(id);
        db.BackupJobs.Remove(existing);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Ok(new { deleted = true, id });
    }

    private static async Task<IResult> RunJobAsync(
        int id,
        HttpRequest httpReq,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var dryRun = false;
        if (httpReq.ContentLength is > 0)
        {
            try
            {
                var body = await httpReq.ReadFromJsonAsync<RunJobRequest>(ct);
                if (body != null) dryRun = body.DryRun;
            }
            catch
            {
                // Ignore empty/malformed optional body
            }
        }

        var execution = await cliRunner.RunJobBackupAsync(id, dryRun, trigger: "manual", ct);
        return Results.Ok(execution);
    }

    private static async Task<IResult> MaintainJobAsync(
        int id,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var execution = await cliRunner.RunJobMaintenanceAsync(id, trigger: "manual", ct);
        return Results.Ok(execution);
    }

    private static async Task<IResult> ToggleJobAsync(
        int id,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        var job = await db.BackupJobs.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Job", id);

        job.IsEnabled = !job.IsEnabled;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);
        await scheduler.SyncJobAsync(id, ct);

        var updated = await db.BackupJobs
            .AsNoTracking()
            .Include(j => j.ConnectionProfile)
            .Include(j => j.StorageProfile)
            .Include(j => j.EncryptionProfile)
            .FirstAsync(j => j.Id == id, ct);

        return Results.Ok(updated);
    }

    private static IResult GetPresets()
    {
        object[] presets =
        [
            new
            {
                id = "standard-production",
                name = "Standard Production",
                badge = "Recommended",
                description = "Daily Full backup at 02:00 UTC with Zstandard (level 3) compression and 7-day retention.",
                config = new
                {
                    strategy = "full",
                    compressionType = "zstd",
                    compressionLevel = 3,
                    compressionThreads = 1,
                    checksum = "md5",
                    splitDb = true,
                    databaseNames = "ALL",
                    scheduleCron = "0 2 * * *",
                    retentionDaily = 7,
                    retentionLast = 7,
                    retentionWeekly = 4,
                    retentionMonthly = 0,
                    retentionYearly = 0,
                    archiveEnabled = false,
                    standaloneMaintenanceOnly = false,
                    maintCheckTables = true,
                    maintOptimize = false,
                    maintVacuum = false,
                    maintAnalyze = false
                }
            },
            new
            {
                id = "high-frequency-hourly",
                name = "High-Frequency Hourly",
                badge = "Low RPO",
                description = "Hourly Incremental backup with weekly Full rollover, keeping 48 hours of granular recovery points.",
                config = new
                {
                    strategy = "incremental",
                    fullEvery = 7,
                    scheduleIntervalMinutes = 60,
                    scheduleCron = "0 * * * *",
                    retentionHourly = 48,
                    retentionLast = 48,
                    retentionDaily = 7,
                    compressionType = "zstd",
                    compressionLevel = 3,
                    compressionThreads = 2,
                    checksum = "md5",
                    splitDb = true,
                    databaseNames = "ALL",
                    archiveEnabled = false,
                    standaloneMaintenanceOnly = false
                }
            },
            new
            {
                id = "cold-archiving-dr",
                name = "Cold Archiving & Disaster Recovery",
                badge = "Compliance",
                description = "Daily Full backup with automatic post-backup archiving to secondary storage, encryption ready, and 30-day retention.",
                config = new
                {
                    strategy = "full",
                    scheduleCron = "0 3 * * *",
                    compressionType = "zstd",
                    compressionLevel = 5,
                    compressionThreads = 2,
                    checksum = "sha1",
                    splitDb = true,
                    databaseNames = "ALL",
                    archiveEnabled = true,
                    archiveLast = 30,
                    archiveWithin = "30d",
                    retentionDaily = 30,
                    retentionWeekly = 8,
                    retentionMonthly = 12,
                    retentionYearly = 2,
                    standaloneMaintenanceOnly = false
                }
            },
            new
            {
                id = "routine-maintenance",
                name = "Routine Database Maintenance",
                badge = "Maintenance",
                description = "Standalone weekly database optimization: Table check, vacuum, analyze, and memory purge without taking a dump.",
                config = new
                {
                    strategy = "full",
                    standaloneMaintenanceOnly = true,
                    maintenanceScheduleCron = "0 4 * * 0",
                    scheduleCron = "0 4 * * 0",
                    maintCheckTables = true,
                    maintOptimize = true,
                    maintVacuum = true,
                    maintAnalyze = true,
                    maintMemoryPurge = true,
                    maintReindex = false,
                    maintCompact = false,
                    archiveEnabled = false
                }
            }
        ];

        return Results.Ok(presets);
    }
}
