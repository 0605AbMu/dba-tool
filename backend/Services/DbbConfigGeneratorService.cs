using System.Text;
using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DbaTool.Api.Services;

public sealed class DbbConfigGeneratorService(
    IServiceScopeFactory scopeFactory,
    IDbaPathProvider pathResolver,
    ILogger<DbbConfigGeneratorService> logger) : IDbbConfigGeneratorService
{
    public string DataDir => pathResolver.DataDir;
    public string ConfigPath => pathResolver.ConfigFilePath;

    public async Task<string> GetYamlContentAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath))
        {
            return await RegenerateYamlAsync(ct);
        }
        return await File.ReadAllTextAsync(ConfigPath, ct);
    }

    public async Task<string> SaveYamlContentAsync(string yamlContent, CancellationToken ct = default)
    {
        try
        {
            var deserializer = new DeserializerBuilder().Build();
            _ = deserializer.Deserialize<object>(yamlContent);
        }
        catch (YamlException ex)
        {
            throw new DomainValidationException($"Invalid YAML syntax: {ex.Message}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        await File.WriteAllTextAsync(ConfigPath, yamlContent, Encoding.UTF8, ct);
        return yamlContent;
    }

    public async Task<string> RegenerateYamlAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connections = await db.ConnectionProfiles.OrderBy(c => c.Id).ToListAsync(ct);
        var storages = await db.StorageProfiles.OrderBy(s => s.Id).ToListAsync(ct);
        var encryptions = await db.EncryptionProfiles.OrderBy(e => e.Id).ToListAsync(ct);
        var jobs = await db.BackupJobs
            .Include(j => j.ConnectionProfile)
            .Include(j => j.StorageProfile)
            .Include(j => j.EncryptionProfile)
            .OrderBy(j => j.Id)
            .ToListAsync(ct);

        var defaultStorage = storages.FirstOrDefault(s => s.IsDefault)
                             ?? storages.FirstOrDefault()
                             ?? new StorageProfile { Name = "default-local", Backend = "filesystem", Path = pathResolver.BackupsDir };

        var tempDir = Path.Combine(DataDir, "tmp");
        var stateDir = Path.Combine(DataDir, "state");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(stateDir);

        var root = new Dictionary<string, object>
        {
            ["temp_dir"] = tempDir,
            ["state"] = new Dictionary<string, object>
            {
                ["dir"] = stateDir
            },
            ["log"] = new Dictionary<string, object>
            {
                ["level"] = "info",
                ["format"] = "json",
                ["type"] = "console",
                ["colour"] = false,
                ["prefix"] = true,
                ["session_id"] = true,
                ["run_id"] = true,
                ["timings"] = true,
                ["size_format"] = "bytes"
            },
            ["defaults"] = new Dictionary<string, object>
            {
                ["backup"] = new Dictionary<string, object>
                {
                    ["strategy"] = "full",
                    ["schema_only"] = false
                },
                ["compression"] = new Dictionary<string, object>
                {
                    ["type"] = "zstd",
                    ["level"] = 3,
                    ["threads"] = 1,
                    ["rsyncable"] = false
                },
                ["checksum"] = "md5",
                ["storage"] = new Dictionary<string, object>
                {
                    ["backend"] = defaultStorage.Backend,
                    ["path"] = defaultStorage.Path,
                    ["file_mode"] = string.IsNullOrWhiteSpace(defaultStorage.FileMode) ? "600" : defaultStorage.FileMode,
                    ["dir_mode"] = string.IsNullOrWhiteSpace(defaultStorage.DirMode) ? "700" : defaultStorage.DirMode
                },
                ["connectivity"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["method"] = "full",
                    ["retry_interval"] = 5,
                    ["timeout"] = 30
                },
                ["retention"] = new Dictionary<string, object>
                {
                    ["last"] = 5,
                    ["within"] = "1440m"
                }
            }
        };

        var connProfilesMap = new Dictionary<string, object>();
        foreach (var conn in connections)
        {
            var cMap = new Dictionary<string, object>
            {
                ["type"] = conn.Type,
                ["host"] = conn.Host
            };
            if (conn.Port > 0) cMap["port"] = conn.Port;
            if (!string.IsNullOrWhiteSpace(conn.Username)) cMap["user"] = conn.Username;
            if (!string.IsNullOrWhiteSpace(conn.Password)) cMap["pass"] = conn.Password;
            if (!string.IsNullOrWhiteSpace(conn.AuthSource)) cMap["auth_source"] = conn.AuthSource;
            if (conn.InfluxVersion > 0) cMap["version"] = conn.InfluxVersion;
            if (!string.IsNullOrWhiteSpace(conn.InfluxMode)) cMap["influx_mode"] = conn.InfluxMode;

            cMap["connectivity"] = new Dictionary<string, object>
            {
                ["enabled"] = !string.Equals(conn.ConnectivityMethod, "none", StringComparison.OrdinalIgnoreCase),
                ["method"] = string.IsNullOrWhiteSpace(conn.ConnectivityMethod) ? "full" : conn.ConnectivityMethod,
                ["retry_interval"] = conn.RetryIntervalSeconds > 0 ? conn.RetryIntervalSeconds : 5,
                ["timeout"] = conn.TimeoutSeconds > 0 ? conn.TimeoutSeconds : 30
            };

            if (conn.TlsEnabled)
            {
                var tlsMap = new Dictionary<string, object>
                {
                    ["enable"] = true,
                    ["verify"] = conn.TlsVerify
                };
                if (!string.IsNullOrWhiteSpace(conn.TlsCaFile)) tlsMap["ca_file"] = conn.TlsCaFile;
                if (!string.IsNullOrWhiteSpace(conn.TlsCertFile)) tlsMap["cert_file"] = conn.TlsCertFile;
                if (!string.IsNullOrWhiteSpace(conn.TlsKeyFile)) tlsMap["key_file"] = conn.TlsKeyFile;
                if (!string.IsNullOrWhiteSpace(conn.TlsVersion)) tlsMap["version"] = conn.TlsVersion;
                cMap["tls"] = tlsMap;
            }

            connProfilesMap[conn.Name] = cMap;
        }

        var dbProfilesMap = new Dictionary<string, object>();
        var maintProfilesMap = new Dictionary<string, object>();

        foreach (var job in jobs)
        {
            var conn = job.ConnectionProfile ?? connections.FirstOrDefault(c => c.Id == job.ConnectionProfileId);
            if (conn == null) continue;

            var dbProfileName = $"{job.Name}-db";
            var isSqlite = string.Equals(conn.Type, "sqlite", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(conn.Type, "sqlite3", StringComparison.OrdinalIgnoreCase);

            List<string> includeList;
            if (isSqlite)
            {
                var rawDb = (job.DatabaseNames ?? "").Trim();
                if (string.IsNullOrEmpty(rawDb)
                    || string.Equals(rawDb, "main", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rawDb, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    includeList = [conn.Host];
                }
                else
                {
                    includeList = SplitCsv(rawDb);
                }
            }
            else
            {
                includeList = SplitCsv(job.DatabaseNames);
                if (includeList.Count == 0) includeList.Add("ALL");
                if (job.PostgresGlobals
                    && conn.Type.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)
                    && !includeList.Contains("__globals__", StringComparer.OrdinalIgnoreCase))
                {
                    includeList.Add("__globals__");
                }
            }

            var dbMap = new Dictionary<string, object>
            {
                ["connection"] = conn.Name,
                ["include"] = includeList
            };

            var excludeList = SplitCsv(job.ExcludeDatabases);
            if (excludeList.Count > 0)
            {
                dbMap["exclude"] = excludeList;
            }

            if (string.Equals(conn.Type, "mysql", StringComparison.OrdinalIgnoreCase)
                || string.Equals(conn.Type, "mariadb", StringComparison.OrdinalIgnoreCase))
            {
                dbMap["routines"] = job.MySqlRoutines;
                dbMap["events"] = job.MySqlEvents;
                dbMap["triggers"] = job.MySqlTriggers;
                dbMap["views"] = job.MySqlViews;
                if (job.MySqlRawBlobs)
                {
                    dbMap["raw_blobs"] = true;
                }
            }

            var tablesInclude = SplitCsv(job.TablesInclude);
            var tablesExclude = SplitCsv(job.TablesExclude);
            var tablesSchemaOnly = SplitCsv(job.TablesSchemaOnly);
            if (tablesInclude.Count > 0 || tablesExclude.Count > 0 || tablesSchemaOnly.Count > 0)
            {
                var tablesMap = new Dictionary<string, object>();
                if (tablesInclude.Count > 0) tablesMap["include"] = tablesInclude;
                if (tablesExclude.Count > 0) tablesMap["exclude"] = tablesExclude;
                if (tablesSchemaOnly.Count > 0) tablesMap["schema_only"] = tablesSchemaOnly;
                dbMap["tables"] = tablesMap;
            }

            dbProfilesMap[dbProfileName] = dbMap;

            if (HasAnyMaintenance(job) || job.StandaloneMaintenanceOnly)
            {
                var mMap = new Dictionary<string, object>();
                if (job.MaintCheckTables) mMap["check_tables"] = true;
                if (job.MaintOptimize) mMap["optimize"] = true;
                if (job.MaintVacuum) mMap["vacuum"] = true;
                if (job.MaintReindex) mMap["reindex"] = true;
                if (job.MaintAnalyze) mMap["analyze"] = true;
                if (job.MaintCompact) mMap["compact"] = true;
                if (job.MaintMemoryPurge) mMap["memory_purge"] = true;
                if (!string.IsNullOrWhiteSpace(job.MaintenanceScheduleCron))
                {
                    mMap["schedule"] = job.MaintenanceScheduleCron.Trim();
                }
                if (mMap.Count == 0)
                {
                    mMap["analyze"] = true;
                }
                maintProfilesMap[$"{job.Name}-maint"] = mMap;
            }
        }

        var storageProfilesMap = new Dictionary<string, object>();
        foreach (var st in storages)
        {
            var sMap = new Dictionary<string, object>
            {
                ["backend"] = st.Backend,
                ["path"] = st.Path,
                ["file_mode"] = string.IsNullOrWhiteSpace(st.FileMode) ? "600" : st.FileMode,
                ["dir_mode"] = string.IsNullOrWhiteSpace(st.DirMode) ? "700" : st.DirMode
            };
            if (!string.IsNullOrWhiteSpace(st.Bucket)) sMap["bucket"] = st.Bucket;
            if (!string.IsNullOrWhiteSpace(st.Region)) sMap["region"] = st.Region;
            if (!string.IsNullOrWhiteSpace(st.Endpoint)) sMap["endpoint"] = st.Endpoint;
            if (!string.IsNullOrWhiteSpace(st.KeyId)) sMap["key_id"] = st.KeyId;
            if (!string.IsNullOrWhiteSpace(st.KeySecret)) sMap["key_secret"] = st.KeySecret;
            if (!string.IsNullOrWhiteSpace(st.Account)) sMap["account"] = st.Account;
            if (!string.IsNullOrWhiteSpace(st.Key)) sMap["key"] = st.Key;
            if (!string.IsNullOrWhiteSpace(st.WebDavUrl)) sMap["url"] = st.WebDavUrl;
            if (!string.IsNullOrWhiteSpace(st.WebDavUser)) sMap["user"] = st.WebDavUser;
            if (!string.IsNullOrWhiteSpace(st.WebDavPass)) sMap["pass"] = st.WebDavPass;
            if (string.Equals(st.Backend, "filesystem", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(st.OwnerUser)) sMap["user"] = st.OwnerUser;
                if (!string.IsNullOrWhiteSpace(st.OwnerGroup)) sMap["group"] = st.OwnerGroup;
            }
            if (st.TlsEnabled)
            {
                var tlsMap = new Dictionary<string, object>
                {
                    ["enable"] = true,
                    ["verify"] = st.TlsVerify
                };
                if (!string.IsNullOrWhiteSpace(st.TlsCaFile)) tlsMap["ca_file"] = st.TlsCaFile;
                if (!string.IsNullOrWhiteSpace(st.TlsCertFile)) tlsMap["cert_file"] = st.TlsCertFile;
                if (!string.IsNullOrWhiteSpace(st.TlsKeyFile)) tlsMap["key_file"] = st.TlsKeyFile;
                if (!string.IsNullOrWhiteSpace(st.TlsVersion)) tlsMap["version"] = st.TlsVersion;
                sMap["tls"] = tlsMap;
            }

            storageProfilesMap[st.Name] = sMap;
        }

        var encProfilesMap = new Dictionary<string, object>();
        foreach (var enc in encryptions)
        {
            if (string.Equals(enc.Type, "none", StringComparison.OrdinalIgnoreCase)) continue;
            var eMap = new Dictionary<string, object>
            {
                ["type"] = enc.Type
            };
            var recipients = SplitCsv(enc.Recipients);
            if (recipients.Count > 0) eMap["recipients"] = recipients;
            if (!string.IsNullOrWhiteSpace(enc.Passphrase)) eMap["passphrase"] = enc.Passphrase;
            encProfilesMap[enc.Name] = eMap;
        }

        var profilesSection = new Dictionary<string, object>
        {
            ["connections"] = connProfilesMap,
            ["databases"] = dbProfilesMap,
            ["storage"] = storageProfilesMap
        };
        if (encProfilesMap.Count > 0) profilesSection["encryption"] = encProfilesMap;
        if (maintProfilesMap.Count > 0) profilesSection["maintenance"] = maintProfilesMap;

        root["profiles"] = profilesSection;

        var jobsList = new List<Dictionary<string, object>>();
        foreach (var job in jobs)
        {
            var st = job.StorageProfile ?? storages.FirstOrDefault(s => s.Id == job.StorageProfileId) ?? defaultStorage;
            var enc = job.EncryptionProfile ?? (job.EncryptionProfileId.HasValue
                ? encryptions.FirstOrDefault(e => e.Id == job.EncryptionProfileId.Value)
                : null);

            object backupValue;
            if (job.StandaloneMaintenanceOnly)
            {
                backupValue = "unset";
            }
            else
            {
                var backupMap = new Dictionary<string, object>
                {
                    ["strategy"] = string.IsNullOrWhiteSpace(job.Strategy) ? "full" : job.Strategy,
                    ["schema_only"] = job.SchemaOnly
                };

                if (job.CreateLatest)
                {
                    backupMap["create_latest"] = true;
                }
                if (job.FullEvery > 0)
                {
                    backupMap["full_every"] = job.FullEvery;
                }
                if (!string.IsNullOrWhiteSpace(job.FullAfter))
                {
                    backupMap["full_after"] = job.FullAfter.Trim();
                }

                // Only emit custom filename template if different from the standard default so SQLite host token is omitted cleanly
                if (!string.IsNullOrWhiteSpace(job.FilenameTemplate)
                    && job.FilenameTemplate != "%type%-%db%-%host%-%strategy%-%timestamp%")
                {
                    backupMap["filename"] = job.FilenameTemplate;
                }

                backupValue = backupMap;
            }

            var jMap = new Dictionary<string, object>
            {
                ["name"] = job.Name,
                ["database"] = $"{job.Name}-db",
                ["split_db"] = job.SplitDb,
                ["backup"] = backupValue,
                ["storage"] = st.Name,
                ["compression"] = new Dictionary<string, object>
                {
                    ["type"] = string.IsNullOrWhiteSpace(job.CompressionType) ? "zstd" : job.CompressionType,
                    ["level"] = job.CompressionLevel > 0 ? job.CompressionLevel : 3,
                    ["threads"] = job.CompressionThreads > 0 ? job.CompressionThreads : 1,
                    ["rsyncable"] = job.CompressionRsyncable
                },
                ["checksum"] = string.IsNullOrWhiteSpace(job.Checksum) ? "md5" : job.Checksum,
                ["log_level"] = string.IsNullOrWhiteSpace(job.LogLevel) ? "info" : job.LogLevel,
                ["progress"] = job.ProgressEnabled,
                ["timings"] = job.TimingsEnabled,
                ["colour"] = job.ColourEnabled,
                ["size_format"] = string.IsNullOrWhiteSpace(job.SizeFormat) ? "bytes" : job.SizeFormat
            };

            if (!string.IsNullOrWhiteSpace(job.LogPath))
            {
                jMap["log_path"] = job.LogPath.Trim();
            }

            if (enc != null && !string.Equals(enc.Type, "none", StringComparison.OrdinalIgnoreCase))
            {
                jMap["encryption"] = enc.Name;
            }

            if (HasAnyMaintenance(job) || job.StandaloneMaintenanceOnly)
            {
                jMap["maintenance"] = $"{job.Name}-maint";
            }

            var retentionMap = new Dictionary<string, object>();
            if (job.RetentionLast > 0) retentionMap["last"] = job.RetentionLast;
            var normalizedWithin = NormalizeWithinToMinutes(job.RetentionWithin);
            if (!string.IsNullOrWhiteSpace(normalizedWithin)) retentionMap["within"] = normalizedWithin;
            if (retentionMap.Count > 0)
            {
                jMap["retention"] = retentionMap;
            }

            if (job.ArchiveEnabled)
            {
                var archiveMap = new Dictionary<string, object>();
                if (job.ArchiveLast > 0) archiveMap["last"] = job.ArchiveLast;
                var archiveWithin = NormalizeWithinToMinutes(job.ArchiveWithin) ?? job.ArchiveWithin?.Trim();
                if (!string.IsNullOrWhiteSpace(archiveWithin)) archiveMap["within"] = archiveWithin;
                if (!string.IsNullOrWhiteSpace(job.ArchivePath)) archiveMap["path"] = job.ArchivePath.Trim();
                if (job.ArchiveStorageProfileId.HasValue)
                {
                    var archiveSt = storages.FirstOrDefault(s => s.Id == job.ArchiveStorageProfileId.Value);
                    if (archiveSt != null)
                    {
                        archiveMap["storage"] = archiveSt.Name;
                    }
                }
                var archiveRetWithin = NormalizeWithinToMinutes(job.ArchiveRetentionWithin) ?? job.ArchiveRetentionWithin?.Trim();
                if (!string.IsNullOrWhiteSpace(archiveRetWithin))
                {
                    archiveMap["retention"] = new Dictionary<string, object>
                    {
                        ["within"] = archiveRetWithin
                    };
                }
                if (archiveMap.Count > 0)
                {
                    jMap["archive"] = archiveMap;
                }
            }

            var scheduleMap = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(job.ScheduleCron))
            {
                scheduleMap["cron"] = job.ScheduleCron.Trim();
            }
            if (job.ScheduleIntervalMinutes > 0)
            {
                scheduleMap["interval"] = job.ScheduleIntervalMinutes;
            }
            if (!string.IsNullOrWhiteSpace(job.ScheduleTime))
            {
                scheduleMap["time"] = job.ScheduleTime.Trim();
            }
            if (!string.IsNullOrWhiteSpace(job.ScheduleBegin))
            {
                scheduleMap["begin"] = job.ScheduleBegin.Trim();
            }
            if (!string.IsNullOrWhiteSpace(job.ScheduleStart))
            {
                scheduleMap["start"] = job.ScheduleStart.Trim();
            }
            if (!string.IsNullOrWhiteSpace(job.ScheduleRecurring))
            {
                scheduleMap["recurring"] = job.ScheduleRecurring.Trim();
            }
            var scheduleDays = SplitCsv(job.ScheduleDays);
            if (scheduleDays.Count > 0)
            {
                scheduleMap["days"] = scheduleDays;
            }
            var excludeDays = SplitCsv(job.ScheduleExcludeDays);
            if (excludeDays.Count > 0)
            {
                scheduleMap["exclude_days"] = excludeDays;
            }
            if (!string.IsNullOrWhiteSpace(job.BlackoutBegin) && !string.IsNullOrWhiteSpace(job.BlackoutEnd))
            {
                var blackoutMap = new Dictionary<string, object>
                {
                    ["begin"] = job.BlackoutBegin.Trim(),
                    ["end"] = job.BlackoutEnd.Trim()
                };
                var blackoutDays = SplitCsv(job.BlackoutDays);
                if (blackoutDays.Count > 0)
                {
                    blackoutMap["days"] = blackoutDays;
                }
                scheduleMap["blackout"] = blackoutMap;
            }

            if (scheduleMap.Count > 0)
            {
                jMap["schedule"] = scheduleMap;
            }

            var preHooks = SplitLinesOrCsv(job.PreHooks);
            var postHooks = SplitLinesOrCsv(job.PostHooks);
            if (preHooks.Count > 0 || postHooks.Count > 0)
            {
                var hooksMap = new Dictionary<string, object>();
                if (preHooks.Count > 0) hooksMap["pre"] = preHooks;
                if (postHooks.Count > 0) hooksMap["post"] = postHooks;
                jMap["hooks"] = hooksMap;
            }

            jobsList.Add(jMap);
        }

        root["jobs"] = jobsList;

        var serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

        var yamlBody = serializer.Serialize(root);
        var header = $"""
            # ============================================================================
            # nfrastack/db-backup (dbb) Configuration
            # Generated automatically by DbaTool.Api at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC
            # ============================================================================

            """;
        var fullYaml = header + yamlBody;

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        await File.WriteAllTextAsync(ConfigPath, fullYaml, Encoding.UTF8, ct);
        logger.LogInformation("Regenerated db-backup configuration at {ConfigPath}", ConfigPath);

        return fullYaml;
    }

    private static bool HasAnyMaintenance(BackupJob job) =>
        job.MaintCheckTables || job.MaintOptimize || job.MaintVacuum ||
        job.MaintReindex || job.MaintAnalyze || job.MaintCompact || job.MaintMemoryPurge;

    private static List<string> SplitCsv(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static List<string> SplitLinesOrCsv(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input
            .Split(['\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    public static string? NormalizeWithinToMinutes(string? within)
    {
        if (string.IsNullOrWhiteSpace(within)) return null;
        var s = within.Trim().ToLowerInvariant();
        if (s.EndsWith('m') && int.TryParse(s[..^1], out var mins) && mins > 0)
            return $"{mins}m";
        if (s.EndsWith('h') && int.TryParse(s[..^1], out var hours) && hours > 0)
            return $"{hours * 60}m";
        if (s.EndsWith('d') && int.TryParse(s[..^1], out var days) && days > 0)
            return $"{days * 1440}m";
        if (int.TryParse(s, out var rawHours) && rawHours > 0)
            return $"{rawHours * 60}m";
        return within.Trim();
    }
}
