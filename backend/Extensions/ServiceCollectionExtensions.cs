using System.Text.Json;
using System.Text.Json.Serialization;
using DbaTool.Api.Abstractions;
using DbaTool.Api.Data;
using DbaTool.Api.Infrastructure;
using DbaTool.Api.Services;
using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDbaToolServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var dataDir = DbaPathResolver.ResolveDataDir(configuration);
        Directory.CreateDirectory(dataDir);
        var sqliteDbPath = Path.Combine(dataDir, "dbatool.db");

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetIsOriginAllowed(_ => true);
            });
        });

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={sqliteDbPath}"));

        services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            });

        services.AddHangfire(cfg => cfg
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseMemoryStorage());

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 4;
            options.SchedulePollingInterval = TimeSpan.FromSeconds(5);
        });

        services.AddHttpClient();

        services.AddSingleton<DbaPathResolver>();
        services.AddSingleton<IDbaPathProvider>(sp => sp.GetRequiredService<DbaPathResolver>());

        services.AddSingleton<DbbConfigGeneratorService>();
        services.AddSingleton<IDbbConfigGeneratorService>(sp => sp.GetRequiredService<DbbConfigGeneratorService>());

        services.AddSingleton<DbbCliRunnerService>();
        services.AddSingleton<IDbbCliRunnerService>(sp => sp.GetRequiredService<DbbCliRunnerService>());

        services.AddSingleton<BackupStorageService>();
        services.AddSingleton<IBackupStorageService>(sp => sp.GetRequiredService<BackupStorageService>());

        services.AddSingleton<HangfireSchedulerService>();
        services.AddSingleton<IHangfireSchedulerService>(sp => sp.GetRequiredService<HangfireSchedulerService>());

        services.AddSingleton<NotificationDispatcherService>();
        services.AddSingleton<INotificationDispatcherService>(sp => sp.GetRequiredService<NotificationDispatcherService>());

        return services;
    }
}
