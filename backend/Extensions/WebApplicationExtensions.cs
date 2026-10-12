using DbaTool.Api.Abstractions;
using DbaTool.Api.Data;
using DbaTool.Api.Endpoints;
using DbaTool.Api.Hubs;
using DbaTool.Api.Infrastructure;
using Hangfire;

namespace DbaTool.Api.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplicationBuilder ConfigureDefaultBindingUrl(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
            string.IsNullOrWhiteSpace(builder.Configuration["urls"]) &&
            string.IsNullOrWhiteSpace(builder.Configuration["Urls"]) &&
            string.IsNullOrWhiteSpace(builder.Configuration["Kestrel:Endpoints:Http:Url"]))
        {
            var port = Environment.GetEnvironmentVariable("PORT") ?? "5150";
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        }

        return builder;
    }

    public static WebApplication UseDbaToolPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseCors();

        // Allow Hangfire dashboard to be embedded in iframes on any localhost port
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Remove("X-Frame-Options");
                context.Response.Headers.Remove("Content-Security-Policy");
                return Task.CompletedTask;
            });
            await next();
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new AllowAllDashboardAuthorizationFilter()],
            DashboardTitle = "nfrastack DBA Control Center — Hangfire Scheduler",
            DisplayStorageConnectionString = false
        });

        return app;
    }

    public static async Task InitializeDbaToolAsync(this WebApplication app, CancellationToken ct = default)
    {
        await DbInitializer.InitializeAsync(app.Services, ct);

        var configGen = app.Services.GetRequiredService<IDbbConfigGeneratorService>();
        await configGen.RegenerateYamlAsync(ct);

        var schedulerSvc = app.Services.GetRequiredService<IHangfireSchedulerService>();
        await schedulerSvc.SyncAllJobsAsync(ct);
    }

    public static WebApplication MapDbaToolEndpoints(this WebApplication app)
    {
        app.MapHub<DbaHub>("/hubs/cli");

        app.MapDashboardEndpoints();
        app.MapConnectionEndpoints();
        app.MapStorageProfileEndpoints();
        app.MapEncryptionProfileEndpoints();
        app.MapNotificationEndpoints();
        app.MapBackupJobEndpoints();
        app.MapBackupArtifactEndpoints();
        app.MapExecutionEndpoints();
        app.MapSchedulerEndpoints();
        app.MapConfigEndpoints();

        app.MapFallbackToFile("index.html");

        return app;
    }
}
