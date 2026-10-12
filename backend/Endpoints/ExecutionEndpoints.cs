using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Endpoints;

public static class ExecutionEndpoints
{
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/executions").WithTags("Execution History");

        group.MapGet("", GetExecutionsAsync);
        group.MapGet("/{id:int}", GetExecutionByIdAsync);
        group.MapPost("/{id:int}/cancel", CancelExecution);

        app.MapPost("/api/cli/execute", ExecuteCustomCliAsync)
            .WithTags("Interactive CLI Studio");

        return app;
    }

    private static async Task<IResult> GetExecutionsAsync(
        int? limit,
        int? jobId,
        AppDbContext db,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 250);
        var query = db.JobExecutions.AsNoTracking();
        if (jobId.HasValue)
        {
            query = query.Where(e => e.JobId == jobId.Value);
        }

        var list = await query
            .OrderByDescending(e => e.StartedAt)
            .Take(take)
            .ToListAsync(ct);

        return Results.Ok(list);
    }

    private static async Task<IResult> GetExecutionByIdAsync(
        int id,
        AppDbContext db,
        CancellationToken ct)
    {
        var exec = await db.JobExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new EntityNotFoundException("Execution", id);

        return Results.Ok(exec);
    }

    private static IResult CancelExecution(
        int id,
        IDbbCliRunnerService cliRunner)
    {
        var cancelled = cliRunner.CancelExecution(id);
        return Results.Ok(new { cancelled, executionId = id });
    }

    private static async Task<IResult> ExecuteCustomCliAsync(
        CustomCliRequest req,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var exec = await cliRunner.ExecuteCustomCliAsync(req, ct);
        return Results.Ok(exec);
    }
}
