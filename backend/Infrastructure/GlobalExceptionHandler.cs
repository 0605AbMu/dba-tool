using DbaTool.Api.Common;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Diagnostics;

namespace DbaTool.Api.Infrastructure;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            EntityNotFoundException or KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found"),
            DomainValidationException or ArgumentException => (StatusCodes.Status400BadRequest, "Validation Error"),
            ResourceInUseException => (StatusCodes.Status400BadRequest, "Resource In Use"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning("Domain exception ({StatusCode}) on {Method} {Path}: {Message}",
                statusCode, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            error = exception.Message,
            message = exception.Message,
            title,
            statusCode
        }, cancellationToken);

        return true;
    }
}

public sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
