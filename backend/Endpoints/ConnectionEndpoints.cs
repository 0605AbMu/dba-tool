using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Endpoints;

public static class ConnectionEndpoints
{
    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/connections").WithTags("Connection Profiles");

        group.MapGet("", GetConnectionsAsync);
        group.MapPost("", CreateConnectionAsync);
        group.MapPut("/{id:int}", UpdateConnectionAsync);
        group.MapDelete("/{id:int}", DeleteConnectionAsync);
        group.MapPost("/{id:int}/test", TestConnectionAsync);
        group.MapGet("/{id:int}/databases", GetDatabasesAsync);

        return app;
    }

    private static async Task<IResult> GetConnectionsAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        var list = await db.ConnectionProfiles
            .AsNoTracking()
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateConnectionAsync(
        ConnectionProfile input,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        input.Id = 0;
        input.Name = SlugHelper.Sanitize(input.Name, "conn");
        input.CreatedAt = DateTime.UtcNow;
        input.UpdatedAt = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(input.AuthSource))
        {
            var eng = (input.Type ?? "").Trim().ToLowerInvariant();
            if (eng is "mssql" or "sqlserver") input.AuthSource = "master";
            else if (eng is "postgres" or "postgresql") input.AuthSource = "postgres";
            else if (eng is "mongo" or "mongodb") input.AuthSource = "admin";
        }

        db.ConnectionProfiles.Add(input);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Created($"/api/connections/{input.Id}", input);
    }

    private static async Task<IResult> UpdateConnectionAsync(
        int id,
        ConnectionProfile input,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var existing = await db.ConnectionProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Connection", id);

        existing.Name = SlugHelper.Sanitize(input.Name, existing.Name);
        existing.Type = input.Type;
        existing.Host = input.Host;
        existing.Port = input.Port;
        existing.Username = input.Username;
        existing.Password = input.Password;
        existing.AuthSource = !string.IsNullOrWhiteSpace(input.AuthSource)
            ? input.AuthSource
            : ((input.Type ?? "").Trim().ToLowerInvariant() is "mssql" or "sqlserver" ? "master"
              : (input.Type ?? "").Trim().ToLowerInvariant() is "postgres" or "postgresql" ? "postgres"
              : (input.Type ?? "").Trim().ToLowerInvariant() is "mongo" or "mongodb" ? "admin"
              : input.AuthSource);
        existing.InfluxVersion = input.InfluxVersion;
        existing.InfluxMode = input.InfluxMode;
        existing.TlsEnabled = input.TlsEnabled;
        existing.TlsCaFile = input.TlsCaFile;
        existing.TlsCertFile = input.TlsCertFile;
        existing.TlsKeyFile = input.TlsKeyFile;
        existing.TlsVerify = input.TlsVerify;
        existing.TlsVersion = input.TlsVersion;
        existing.ConnectivityMethod = input.ConnectivityMethod;
        existing.RetryIntervalSeconds = input.RetryIntervalSeconds > 0 ? input.RetryIntervalSeconds : 5;
        existing.TimeoutSeconds = input.TimeoutSeconds > 0 ? input.TimeoutSeconds : 30;
        existing.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteConnectionAsync(
        int id,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var existing = await db.ConnectionProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Connection", id);

        var inUse = await db.BackupJobs.AsNoTracking().AnyAsync(j => j.ConnectionProfileId == id, ct);
        if (inUse)
        {
            throw new ResourceInUseException("Cannot delete connection profile because it is used by one or more backup jobs.");
        }

        db.ConnectionProfiles.Remove(existing);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Ok(new { deleted = true, id });
    }

    private static async Task<IResult> TestConnectionAsync(
        int id,
        AppDbContext db,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var conn = await db.ConnectionProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new EntityNotFoundException("Connection", id);

        var result = await cliRunner.TestConnectionAsync(conn, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetDatabasesAsync(
        int id,
        AppDbContext db,
        CancellationToken ct)
    {
        var conn = await db.ConnectionProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new EntityNotFoundException("Connection", id);

        var engine = (conn.Type ?? "").Trim().ToLowerInvariant();
        var databases = new List<string>();
        string message;

        switch (engine)
        {
            case "sqlite" or "sqlite3":
                databases.Add("main");
                if (!string.IsNullOrWhiteSpace(conn.Host) && File.Exists(conn.Host))
                {
                    try
                    {
                        using var sqliteConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={conn.Host}");
                        await sqliteConn.OpenAsync(ct);
                        using var cmd = sqliteConn.CreateCommand();
                        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                        using var reader = await cmd.ExecuteReaderAsync(ct);
                        var tables = new List<string>();
                        while (await reader.ReadAsync(ct))
                        {
                            tables.Add(reader.GetString(0));
                        }
                        message = $"SQLite database found at '{conn.Host}' with {tables.Count} tables.";
                    }
                    catch (Exception ex)
                    {
                        message = $"SQLite file located at '{conn.Host}', but inspection returned: {ex.Message}";
                    }
                }
                else
                {
                    message = string.IsNullOrWhiteSpace(conn.Host)
                        ? "SQLite database path not set."
                        : $"SQLite database file not yet created at '{conn.Host}' (will be initialized on first write).";
                }
                break;

            case "redis":
                databases = Enumerable.Range(0, 16).Select(i => i.ToString()).ToList();
                message = "Standard Redis database namespaces (0-15).";
                break;

            case "postgres" or "postgresql":
                try
                {
                    var port = conn.Port > 0 ? conn.Port : 5432;
                    var pass = ResolveSecret(conn.Password);
                    var authDb = !string.IsNullOrWhiteSpace(conn.AuthSource) ? conn.AuthSource : "postgres";
                    var cs = $"Host={conn.Host};Port={port};Username={conn.Username};Password={pass};Database={authDb};Timeout=5;";
                    using var npgConn = new Npgsql.NpgsqlConnection(cs);
                    await npgConn.OpenAsync(ct);
                    using var cmd = npgConn.CreateCommand();
                    cmd.CommandText = "SELECT datname FROM pg_database WHERE datistemplate = false AND datname NOT IN ('postgres') ORDER BY datname;";
                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        databases.Add(reader.GetString(0));
                    }
                    message = $"Discovered {databases.Count} database(s) on PostgreSQL server '{conn.Host}'.";
                }
                catch (Exception ex)
                {
                    message = $"PostgreSQL discovery failed: {ex.Message}";
                }
                break;

            case "mysql" or "mariadb":
                try
                {
                    var port = conn.Port > 0 ? conn.Port : 3306;
                    var pass = ResolveSecret(conn.Password);
                    var cs = $"Server={conn.Host};Port={port};User ID={conn.Username};Password={pass};ConnectionTimeout=5;";
                    using var myConn = new MySqlConnector.MySqlConnection(cs);
                    await myConn.OpenAsync(ct);
                    using var cmd = myConn.CreateCommand();
                    cmd.CommandText = "SHOW DATABASES;";
                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        var dbName = reader.GetString(0);
                        if (!new[] { "information_schema", "performance_schema", "mysql", "sys" }.Contains(dbName, StringComparer.OrdinalIgnoreCase))
                        {
                            databases.Add(dbName);
                        }
                    }
                    message = $"Discovered {databases.Count} database(s) on MySQL server '{conn.Host}'.";
                }
                catch (Exception ex)
                {
                    message = $"MySQL discovery failed: {ex.Message}";
                }
                break;

            case "mssql" or "sqlserver":
                try
                {
                    var port = conn.Port > 0 ? conn.Port : 1433;
                    var pass = ResolveSecret(conn.Password);
                    var cs = $"Server={conn.Host},{port};User Id={conn.Username};Password={pass};TrustServerCertificate=True;Connect Timeout=5;Database=master;";
                    using var sqlConn = new Microsoft.Data.SqlClient.SqlConnection(cs);
                    await sqlConn.OpenAsync(ct);
                    using var cmd = sqlConn.CreateCommand();
                    cmd.CommandText = "SELECT name FROM sys.databases WHERE state = 0 AND name NOT IN ('master', 'tempdb', 'model', 'msdb') ORDER BY name;";
                    using var reader = await cmd.ExecuteReaderAsync(ct);
                    while (await reader.ReadAsync(ct))
                    {
                        databases.Add(reader.GetString(0));
                    }
                    message = $"Discovered {databases.Count} database(s) on MSSQL server '{conn.Host}'.";
                }
                catch (Exception ex)
                {
                    message = $"MSSQL discovery failed: {ex.Message}";
                }
                break;

            case "mongo" or "mongodb":
                databases = ["admin", "local", "config"];
                message = "Standard MongoDB system databases.";
                break;

            case "couch" or "couchdb":
                databases = ["_users", "_replicator", "_global_changes"];
                message = "Standard CouchDB system databases.";
                break;

            case "influx" or "influxdb":
                databases = ["_internal"];
                message = "Standard InfluxDB default catalog.";
                break;

            default:
                databases = ["main"];
                message = $"Default database namespace for engine '{engine}'.";
                break;
        }

        return Results.Ok(new
        {
            success = databases.Count > 0,
            databases,
            message
        });
    }

    private static string ResolveSecret(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (raw.StartsWith("env://", StringComparison.OrdinalIgnoreCase))
        {
            return Environment.GetEnvironmentVariable(raw[6..]) ?? string.Empty;
        }
        if (raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var path = raw[7..];
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        return raw;
    }
}
