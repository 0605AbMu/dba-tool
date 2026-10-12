using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Endpoints;

public static class StorageProfileEndpoints
{
    public static IEndpointRouteBuilder MapStorageProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/storage-profiles").WithTags("Storage Profiles");

        group.MapGet("", GetStorageProfilesAsync);
        group.MapPost("", CreateStorageProfileAsync);
        group.MapPut("/{id:int}", UpdateStorageProfileAsync);
        group.MapDelete("/{id:int}", DeleteStorageProfileAsync);
        group.MapPost("/{id:int}/test", TestStorageProfileAsync);

        return app;
    }

    private static async Task<IResult> GetStorageProfilesAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        var list = await db.StorageProfiles
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateStorageProfileAsync(
        StorageProfile input,
        AppDbContext db,
        IDbaPathProvider paths,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        input.Id = 0;
        input.Name = SlugHelper.Sanitize(input.Name, "storage");
        input.CreatedAt = DateTime.UtcNow;

        if (input.IsDefault)
        {
            await db.StorageProfiles.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false), ct);
        }

        if (string.Equals(input.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(input.Path))
            {
                input.Path = paths.BackupsDir;
            }
            Directory.CreateDirectory(input.Path);
        }

        db.StorageProfiles.Add(input);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Created($"/api/storage-profiles/{input.Id}", input);
    }

    private static async Task<IResult> UpdateStorageProfileAsync(
        int id,
        StorageProfile input,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var existing = await db.StorageProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Storage profile", id);

        if (input.IsDefault && !existing.IsDefault)
        {
            await db.StorageProfiles.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false), ct);
        }

        existing.Name = SlugHelper.Sanitize(input.Name, existing.Name);
        existing.Backend = input.Backend;
        existing.Path = input.Path;
        existing.Bucket = input.Bucket;
        existing.Region = input.Region;
        existing.Endpoint = input.Endpoint;
        existing.KeyId = input.KeyId;
        existing.KeySecret = input.KeySecret;
        existing.Account = input.Account;
        existing.Key = input.Key;
        existing.WebDavUrl = input.WebDavUrl;
        existing.WebDavUser = input.WebDavUser;
        existing.WebDavPass = input.WebDavPass;
        existing.FileMode = string.IsNullOrWhiteSpace(input.FileMode) ? "600" : input.FileMode;
        existing.DirMode = string.IsNullOrWhiteSpace(input.DirMode) ? "700" : input.DirMode;
        existing.OwnerUser = input.OwnerUser;
        existing.OwnerGroup = input.OwnerGroup;
        existing.TlsEnabled = input.TlsEnabled;
        existing.TlsCaFile = input.TlsCaFile;
        existing.TlsCertFile = input.TlsCertFile;
        existing.TlsKeyFile = input.TlsKeyFile;
        existing.TlsVerify = input.TlsVerify;
        existing.TlsVersion = input.TlsVersion;
        existing.IsDefault = input.IsDefault;

        if (string.Equals(existing.Backend, "filesystem", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(existing.Path))
        {
            Directory.CreateDirectory(existing.Path);
        }

        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteStorageProfileAsync(
        int id,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var existing = await db.StorageProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Storage profile", id);

        var inUse = await db.BackupJobs.AsNoTracking().AnyAsync(j => j.StorageProfileId == id, ct);
        if (inUse)
        {
            throw new ResourceInUseException("Cannot delete storage profile because it is used by one or more backup jobs.");
        }

        db.StorageProfiles.Remove(existing);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Ok(new { deleted = true, id });
    }

    private static async Task<IResult> TestStorageProfileAsync(
        int id,
        AppDbContext db,
        IDbaPathProvider paths,
        CancellationToken ct)
    {
        var storage = await db.StorageProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new EntityNotFoundException("Storage profile", id);

        var backend = (storage.Backend ?? "filesystem").ToLowerInvariant().Trim();

        if (backend == "filesystem")
        {
            try
            {
                var targetPath = string.IsNullOrWhiteSpace(storage.Path) ? paths.BackupsDir : storage.Path;
                Directory.CreateDirectory(targetPath);

                var probeFile = Path.Combine(targetPath, $".probe_{Guid.NewGuid():N}.tmp");
                await File.WriteAllTextAsync(probeFile, "test-probe-access", ct);
                File.Delete(probeFile);

                long? freeSpace = null;
                try
                {
                    var fullPath = Path.GetFullPath(targetPath);
                    var root = Path.GetPathRoot(fullPath);
                    if (!string.IsNullOrEmpty(root))
                    {
                        var drive = new DriveInfo(root);
                        if (drive.IsReady)
                        {
                            freeSpace = drive.AvailableFreeSpace;
                        }
                    }
                }
                catch
                {
                    // DriveInfo might fail on some virtual / containerized mounts
                }

                return Results.Ok(new
                {
                    accessible = true,
                    message = $"Filesystem storage path '{targetPath}' is valid and writable.",
                    freeSpaceBytes = freeSpace
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new
                {
                    accessible = false,
                    message = $"Filesystem access failed: {ex.Message}",
                    freeSpaceBytes = (long?)null
                });
            }
        }

        // Remote storage profiles: check network / endpoint accessibility
        try
        {
            string host = "";
            int port = 443;

            if (backend == "webdav")
            {
                if (!string.IsNullOrWhiteSpace(storage.WebDavUrl) && Uri.TryCreate(storage.WebDavUrl, UriKind.Absolute, out var webdavUri))
                {
                    host = webdavUri.Host;
                    port = webdavUri.Port > 0 ? webdavUri.Port : (webdavUri.Scheme == "https" ? 443 : 80);
                }
            }
            else if (backend is "s3" or "azure" or "gcs")
            {
                if (!string.IsNullOrWhiteSpace(storage.Endpoint) && Uri.TryCreate(storage.Endpoint, UriKind.Absolute, out var epUri))
                {
                    host = epUri.Host;
                    port = epUri.Port > 0 ? epUri.Port : (epUri.Scheme == "https" ? 443 : 80);
                }
                else if (!string.IsNullOrWhiteSpace(storage.Endpoint))
                {
                    host = storage.Endpoint;
                }
                else if (backend == "s3")
                {
                    var region = string.IsNullOrWhiteSpace(storage.Region) ? "us-east-1" : storage.Region;
                    host = $"s3.{region}.amazonaws.com";
                }
            }

            if (string.IsNullOrWhiteSpace(host))
            {
                return Results.Ok(new
                {
                    accessible = false,
                    message = $"Cannot test {backend} profile: endpoint or host could not be determined.",
                    freeSpaceBytes = (long?)null
                });
            }

            using var tcpClient = new System.Net.Sockets.TcpClient();
            var connectTask = tcpClient.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(3000, ct));

            if (completed == connectTask && tcpClient.Connected)
            {
                return Results.Ok(new
                {
                    accessible = true,
                    message = $"Successfully verified connectivity to {backend} host '{host}:{port}'.",
                    freeSpaceBytes = (long?)null
                });
            }

            return Results.Ok(new
            {
                accessible = false,
                message = $"Connection timed out attempting to reach {backend} endpoint '{host}:{port}'.",
                freeSpaceBytes = (long?)null
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                accessible = false,
                message = $"Connectivity check error for {backend}: {ex.Message}",
                freeSpaceBytes = (long?)null
            });
        }
    }
}
