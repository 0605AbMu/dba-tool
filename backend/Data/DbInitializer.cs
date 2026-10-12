using DbaTool.Api.Abstractions;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var paths = services.GetRequiredService<IDbaPathProvider>();
        paths.EnsureDirectories();

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync(ct);

        // Fallback SQL to ensure NotificationProfiles table exists on existing SQLite databases
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""NotificationProfiles"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_NotificationProfiles"" PRIMARY KEY AUTOINCREMENT,
                ""Name"" TEXT NOT NULL,
                ""Channel"" TEXT NOT NULL,
                ""IsEnabled"" INTEGER NOT NULL,
                ""NotifyOnSuccess"" INTEGER NOT NULL,
                ""NotifyOnFailure"" INTEGER NOT NULL,
                ""BotToken"" TEXT NULL,
                ""ChatId"" TEXT NULL,
                ""WebhookUrl"" TEXT NULL,
                ""SmtpHost"" TEXT NULL,
                ""SmtpPort"" INTEGER NOT NULL,
                ""SmtpUsername"" TEXT NULL,
                ""SmtpPassword"" TEXT NULL,
                ""SmtpUseSsl"" INTEGER NOT NULL,
                ""EmailFrom"" TEXT NULL,
                ""EmailTo"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
        ", ct);

        try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""JobExecutions"" ADD COLUMN ""CurrentStage"" TEXT;", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""JobExecutions"" ADD COLUMN ""ErrorSummary"" TEXT;", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(@"ALTER TABLE ""JobExecutions"" ADD COLUMN ""ErrorDetails"" TEXT;", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(@"UPDATE ""ConnectionProfiles"" SET ""AuthSource"" = 'master' WHERE LOWER(""Type"") IN ('mssql', 'sqlserver') AND (""AuthSource"" IS NULL OR ""AuthSource"" = '');", ct); } catch { }
        try { await db.Database.ExecuteSqlRawAsync(@"UPDATE ""ConnectionProfiles"" SET ""AuthSource"" = 'postgres' WHERE LOWER(""Type"") IN ('postgres', 'postgresql') AND (""AuthSource"" IS NULL OR ""AuthSource"" = '');", ct); } catch { }

        // Reset any stale 'running' executions from previous process restarts
        var staleRunning = await db.JobExecutions
            .Where(e => e.Status == "running")
            .ToListAsync(ct);

        if (staleRunning.Count > 0)
        {
            foreach (var exec in staleRunning)
            {
                exec.Status = "cancelled";
                exec.FinishedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(ct);
        }

        // Seed only a portable default local filesystem StorageProfile if none exists
        if (!await db.StorageProfiles.AnyAsync(ct))
        {
            db.StorageProfiles.Add(new StorageProfile
            {
                Name = "default-local",
                Backend = "filesystem",
                Path = paths.BackupsDir,
                FileMode = "600",
                DirMode = "700",
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            var defaultLocal = await db.StorageProfiles
                .FirstOrDefaultAsync(s => s.Name == "default-local" && s.Backend == "filesystem", ct);
            if (defaultLocal != null && string.IsNullOrWhiteSpace(defaultLocal.Path))
            {
                defaultLocal.Path = paths.BackupsDir;
                await db.SaveChangesAsync(ct);
            }
        }
    }
}
