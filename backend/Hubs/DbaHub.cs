using DbaTool.Api.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace DbaTool.Api.Hubs;

public sealed class DbaHub(IDbbCliRunnerService cliRunner, ILogger<DbaHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        await Clients.Caller.SendAsync("Connected", new
        {
            connectionId = Context.ConnectionId,
            serverTime = DateTime.UtcNow
        });
        await base.OnConnectedAsync();
    }

    public Task<bool> CancelExecution(int executionId)
    {
        var cancelled = cliRunner.CancelExecution(executionId);
        return Task.FromResult(cancelled);
    }

    public Task<string> Ping() => Task.FromResult("pong");
}
