using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;

namespace DbaTool.Api.Endpoints;

public static class SchedulerEndpoints
{
    public static IEndpointRouteBuilder MapSchedulerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scheduler/jobs").WithTags("Hangfire Scheduler");

        group.MapGet("", GetScheduledJobsAsync);
        group.MapPost("/{jobId:int}/pause", PauseScheduledJobAsync);
        group.MapPost("/{jobId:int}/resume", ResumeScheduledJobAsync);
        group.MapPost("/{jobId:int}/trigger", TriggerScheduledJobAsync);

        return app;
    }

    private static async Task<IResult> GetScheduledJobsAsync(
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        var jobs = await scheduler.GetScheduledJobsAsync(ct);
        return Results.Ok(jobs);
    }

    private static async Task<IResult> PauseScheduledJobAsync(
        int jobId,
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        var updated = await scheduler.PauseJobAsync(jobId, ct)
            ?? throw new EntityNotFoundException("Scheduled job", jobId);

        return Results.Ok(updated);
    }

    private static async Task<IResult> ResumeScheduledJobAsync(
        int jobId,
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        var updated = await scheduler.ResumeJobAsync(jobId, ct)
            ?? throw new EntityNotFoundException("Scheduled job", jobId);

        return Results.Ok(updated);
    }

    private static async Task<IResult> TriggerScheduledJobAsync(
        int jobId,
        IDbbCliRunnerService cliRunner,
        IHangfireSchedulerService scheduler,
        CancellationToken ct)
    {
        var execution = await cliRunner.RunJobBackupAsync(jobId, dryRun: false, trigger: "scheduled", ct);
        await scheduler.SyncJobAsync(jobId, ct);
        return Results.Ok(execution);
    }
}
