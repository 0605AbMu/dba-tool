using DbaTool.Api.Abstractions;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Dashboard & System");

        group.MapGet("/dashboard/summary", GetDashboardSummaryAsync);
        group.MapGet("/system/info", GetSystemInfoAsync);
        group.MapGet("/system/version-check", CheckVersionAsync);
        group.MapGet("/system/license", GetLicenseInfoAsync);
        group.MapPost("/system/license/check", CheckLicenseFeatureAsync);
        group.MapPost("/system/license/install", InstallLicenseAsync);
        group.MapGet("/system/stats", GetStatsAsync);

        return app;
    }

    private static async Task<IResult> GetDashboardSummaryAsync(
        AppDbContext db,
        IBackupStorageService storageSvc,
        IHangfireSchedulerService scheduler,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var totalJobs = await db.BackupJobs.AsNoTracking().CountAsync(ct);
        var activeJobs = await db.BackupJobs.AsNoTracking().CountAsync(j => j.IsEnabled, ct);
        var totalConnections = await db.ConnectionProfiles.AsNoTracking().CountAsync(ct);
        var totalStorages = await db.StorageProfiles.AsNoTracking().CountAsync(ct);
        var totalEncryptions = await db.EncryptionProfiles.AsNoTracking().CountAsync(ct);
        var totalNotifications = await db.NotificationProfiles.AsNoTracking().CountAsync(ct);

        var backups = await storageSvc.ListBackupsAsync(ct: ct);
        var totalStorageBytes = backups.Sum(b => b.SizeBytes);
        var totalRawBytes = backups.Sum(b => b.RawSizeBytes > 0 ? b.RawSizeBytes : b.SizeBytes);

        var totalExecutions = await db.JobExecutions.AsNoTracking().CountAsync(ct);
        var completedExecutions = await db.JobExecutions.AsNoTracking().CountAsync(e => e.Status == "completed", ct);
        var failedExecutions = await db.JobExecutions.AsNoTracking().CountAsync(e => e.Status == "failed", ct);
        var runningExecutions = await db.JobExecutions.AsNoTracking().CountAsync(e => e.Status == "running", ct);
        var finishedCount = completedExecutions + failedExecutions;
        var successRate = finishedCount > 0
            ? Math.Round((double)completedExecutions / finishedCount * 100.0, 1)
            : 100.0;

        var recentExecutions = await db.JobExecutions
            .AsNoTracking()
            .OrderByDescending(e => e.StartedAt)
            .Take(10)
            .ToListAsync(ct);

        var schedules = await scheduler.GetScheduledJobsAsync(ct);
        var sysInfo = await cliRunner.GetSystemInfoAsync(ct);

        return Results.Ok(new DashboardSummaryDto
        {
            TotalJobs = totalJobs,
            ActiveJobs = activeJobs,
            TotalConnections = totalConnections,
            TotalStorageProfiles = totalStorages,
            TotalEncryptionProfiles = totalEncryptions,
            TotalNotificationProfiles = totalNotifications,
            TotalBackups = backups.Count,
            TotalStorageBytes = totalStorageBytes,
            TotalRawBytes = totalRawBytes,
            TotalExecutions = totalExecutions,
            CompletedExecutions = completedExecutions,
            FailedExecutions = failedExecutions,
            RunningExecutions = runningExecutions,
            SuccessRate = successRate,
            RecentExecutions = recentExecutions,
            UpcomingSchedules = schedules,
            RecentBackups = backups.Take(6).ToList(),
            SystemInfo = sysInfo
        });
    }

    private static async Task<IResult> GetSystemInfoAsync(
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var info = await cliRunner.GetSystemInfoAsync(ct);
        return Results.Ok(info);
    }

    private static async Task<IResult> CheckVersionAsync(
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var result = await cliRunner.CheckVersionAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetLicenseInfoAsync(
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var result = await cliRunner.GetLicenseInfoAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CheckLicenseFeatureAsync(
        LicenseCheckRequest req,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var result = await cliRunner.CheckLicenseFeatureAsync(req.Feature, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> InstallLicenseAsync(
        LicenseInstallRequest req,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var result = await cliRunner.InstallLicenseAsync(req, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetStatsAsync(
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var result = await cliRunner.GetStatsAsync(ct);
        return Results.Ok(result);
    }
}
