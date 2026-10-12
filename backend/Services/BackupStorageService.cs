using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DbaTool.Api.Abstractions;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Services;

public sealed partial class BackupStorageService(
    IServiceScopeFactory scopeFactory,
    IDbaPathProvider pathResolver,
    ILogger<BackupStorageService> logger) : IBackupStorageService
{
    private static readonly string[] ValidBackupExtensions =
    [
        ".sql", ".zst", ".gz", ".bz2", ".xz",
        ".age", ".gpg", ".enc",
        ".mongo", ".redis", ".influx", ".couch"
    ];

    public string DataDir => pathResolver.DataDir;

    [GeneratedRegex(@"^(?<type>[^-]+)-(?<db>.+?)-(?<host>[^-]+)-(?<strategy>full|incr|diff)-(?<ts>\d{8}-\d{6})", RegexOptions.IgnoreCase)]
    private static partial Regex BackupWithHostRegex();

    [GeneratedRegex(@"^(?<type>[^-]+)-(?<db>.+?)-(?<strategy>full|incr|diff)-(?<ts>\d{8}-\d{6})", RegexOptions.IgnoreCase)]
    private static partial Regex BackupNoHostRegex();

    public async Task<List<BackupArtifactDto>> ListBackupsAsync(int? storageProfileId = null, string? prefix = null, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<StorageProfile> profiles;
        if (storageProfileId.HasValue)
        {
            var found = await db.StorageProfiles.FindAsync([storageProfileId.Value], ct);
            profiles = found != null ? [found] : [];
        }
        else
        {
            profiles = await db.StorageProfiles
                .Where(s => s.Backend == "filesystem")
                .OrderByDescending(s => s.IsDefault)
                .ToListAsync(ct);
        }

        if (profiles.Count == 0)
        {
            profiles.Add(new StorageProfile
            {
                Id = 0,
                Name = "default-local",
                Backend = "filesystem",
                Path = pathResolver.BackupsDir
            });
        }

        var results = new List<BackupArtifactDto>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            if (!string.Equals(profile.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Path) || !Directory.Exists(profile.Path))
            {
                continue;
            }

            var dirInfo = new DirectoryInfo(profile.Path);
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if (!IsBackupArtifactFile(file.Name))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(prefix)
                    && !file.Name.Contains(prefix.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!seenPaths.Add(file.FullName))
                {
                    continue;
                }

                var dto = await BuildBackupArtifactDtoAsync(file, profile, ct);
                results.Add(dto);
            }
        }

        return results
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Filename)
            .ToList();
    }

    public async Task<SidecarMetadataDto?> GetSidecarAsync(string filename, int? storageProfileId = null, CancellationToken ct = default)
    {
        var filePath = await GetBackupFilePathAsync(filename, storageProfileId, ct);
        if (filePath == null) return null;

        var sidecarPath = filePath + ".json";
        if (!File.Exists(sidecarPath)) return null;

        var json = await File.ReadAllTextAsync(sidecarPath, ct);
        return JsonSerializer.Deserialize<SidecarMetadataDto>(json);
    }

    public async Task<string?> GetBackupFilePathAsync(string filename, int? storageProfileId = null, CancellationToken ct = default)
    {
        var safeName = Path.GetFileName(filename);
        if (string.IsNullOrWhiteSpace(safeName)) return null;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        StorageProfile? storage = null;
        if (storageProfileId.HasValue)
        {
            storage = await db.StorageProfiles.FindAsync([storageProfileId.Value], ct);
        }
        storage ??= await db.StorageProfiles.FirstOrDefaultAsync(s => s.IsDefault && s.Backend == "filesystem", ct)
                    ?? await db.StorageProfiles.FirstOrDefaultAsync(s => s.Backend == "filesystem", ct);

        var baseDir = storage?.Path ?? pathResolver.BackupsDir;
        var candidate = Path.Combine(baseDir, safeName);
        return File.Exists(candidate) ? candidate : null;
    }

    public async Task<bool> DeleteBackupAsync(string filename, int? storageProfileId = null, CancellationToken ct = default)
    {
        var filePath = await GetBackupFilePathAsync(filename, storageProfileId, ct);
        if (filePath == null || !File.Exists(filePath))
        {
            return false;
        }

        File.Delete(filePath);
        foreach (var ext in new[] { ".json", ".md5", ".sha1" })
        {
            var companion = filePath + ext;
            if (File.Exists(companion))
            {
                File.Delete(companion);
            }
        }
        return true;
    }

    private async Task<BackupArtifactDto> BuildBackupArtifactDtoAsync(FileInfo file, StorageProfile profile, CancellationToken ct)
    {
        var dto = new BackupArtifactDto
        {
            Filename = file.Name,
            FullPath = file.FullName,
            StorageProfileId = profile.Id,
            StorageProfileName = profile.Name,
            StorageBackend = profile.Backend,
            SizeBytes = file.Length,
            CreatedAt = file.LastWriteTimeUtc,
            Compression = InferCompression(file.Name),
            Encryption = InferEncryption(file.Name),
            HasSidecar = File.Exists(file.FullName + ".json"),
            HasMd5File = File.Exists(file.FullName + ".md5"),
            HasSha1File = File.Exists(file.FullName + ".sha1")
        };

        // Parse filename tokens first as baseline
        ParseFilenameIntoDto(file.Name, dto);

        if (dto.HasSidecar)
        {
            try
            {
                var sidecarJson = await File.ReadAllTextAsync(file.FullName + ".json");
                var sidecar = JsonSerializer.Deserialize<SidecarMetadataDto>(sidecarJson);
                if (sidecar != null)
                {
                    dto.Sidecar = sidecar;
                    if (!string.IsNullOrWhiteSpace(sidecar.Type)) dto.Type = sidecar.Type;
                    if (!string.IsNullOrWhiteSpace(sidecar.Db)) dto.Database = Path.GetFileName(sidecar.Db);
                    if (!string.IsNullOrWhiteSpace(sidecar.Host)) dto.Host = sidecar.Host;
                    if (!string.IsNullOrWhiteSpace(sidecar.Strategy)) dto.Strategy = sidecar.Strategy;
                    if (sidecar.RawSize > 0) dto.RawSizeBytes = sidecar.RawSize;
                    if (!string.IsNullOrWhiteSpace(sidecar.Compress)) dto.Compression = sidecar.Compress;
                    dto.CompressionLevel = sidecar.CompressionLevel;
                    dto.SchemaOnly = sidecar.SchemaOnly;
                    dto.DurationMs = sidecar.DurationMs;
                    dto.JobName = sidecar.Job?.Name;

                    if (sidecar.Encryption != null && !string.IsNullOrWhiteSpace(sidecar.Encryption.Type))
                    {
                        dto.Encryption = sidecar.Encryption.Type;
                    }

                    if (sidecar.Server != null && !string.IsNullOrWhiteSpace(sidecar.Server.Engine))
                    {
                        dto.ServerVersion = $"{sidecar.Server.Engine} {sidecar.Server.Version}".Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(sidecar.Timestamp)
                        && DateTime.TryParse(sidecar.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedTs))
                    {
                        dto.CreatedAt = parsedTs;
                    }

                    if (sidecar.Checksums != null && sidecar.Checksums.Count > 0)
                    {
                        var preferred = sidecar.Checksums.FirstOrDefault(kv => kv.Key.StartsWith("uncompressed_", StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrEmpty(preferred.Key))
                        {
                            preferred = sidecar.Checksums.First();
                        }
                        dto.ChecksumAlgorithm = preferred.Key
                            .Replace("uncompressed_", "", StringComparison.OrdinalIgnoreCase)
                            .Replace("compressed_", "", StringComparison.OrdinalIgnoreCase);
                        dto.ChecksumValue = preferred.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse sidecar JSON for {FileName}", file.Name);
            }
        }

        if (dto.RawSizeBytes > 0 && dto.SizeBytes > 0)
        {
            var ratio = (1.0 - ((double)dto.SizeBytes / dto.RawSizeBytes)) * 100.0;
            dto.CompressionRatio = Math.Round(Math.Max(0, ratio), 1);
        }

        return dto;
    }

    private static void ParseFilenameIntoDto(string filename, BackupArtifactDto dto)
    {
        var mHost = BackupWithHostRegex().Match(filename);
        if (mHost.Success)
        {
            dto.Type = mHost.Groups["type"].Value;
            dto.Database = mHost.Groups["db"].Value;
            dto.Host = mHost.Groups["host"].Value;
            dto.Strategy = NormalizeStrategy(mHost.Groups["strategy"].Value);
            if (DateTime.TryParseExact(mHost.Groups["ts"].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts))
            {
                dto.CreatedAt = ts.ToUniversalTime();
            }
            return;
        }

        var mNoHost = BackupNoHostRegex().Match(filename);
        if (mNoHost.Success)
        {
            dto.Type = mNoHost.Groups["type"].Value;
            dto.Database = mNoHost.Groups["db"].Value;
            dto.Host = "local";
            dto.Strategy = NormalizeStrategy(mNoHost.Groups["strategy"].Value);
            if (DateTime.TryParseExact(mNoHost.Groups["ts"].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts))
            {
                dto.CreatedAt = ts.ToUniversalTime();
            }
        }
    }

    private static string NormalizeStrategy(string strat) => strat.ToLowerInvariant() switch
    {
        "incr" => "incremental",
        "diff" => "differential",
        _ => "full"
    };

    private static bool IsBackupArtifactFile(string name)
    {
        if (name.StartsWith("latest-", StringComparison.OrdinalIgnoreCase) || name.StartsWith('.'))
        {
            return false;
        }
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".md5", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".sha1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return ValidBackupExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private static string InferCompression(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains(".zst")) return "zstd";
        if (lower.Contains(".gz")) return "gz";
        if (lower.Contains(".bz2")) return "bz";
        if (lower.Contains(".xz")) return "xz";
        return "none";
    }

    private static string InferEncryption(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith(".age")) return "age";
        if (lower.EndsWith(".gpg")) return "gpg";
        if (lower.EndsWith(".enc")) return "openssl";
        return "none";
    }
}
