using System.Text.Json;
using System.Text.Json.Serialization;

namespace DbaTool.Api.Models;

public record RunJobRequest(bool DryRun = false);

public record VerifyBackupRequest(
    string Filename,
    int? StorageProfileId = null,
    string? Passphrase = null,
    string? Identity = null
);

public record RestoreBackupRequest(
    string Filename,
    int? StorageProfileId = null,
    int? ConnectionProfileId = null,
    string? TargetDatabase = null,
    bool CreateDb = true,
    bool DryRun = false,
    string? Passphrase = null,
    string? Identity = null,
    string? Base = null,
    string? Compress = null,
    string? Encryption = null,
    string? Profile = null,
    string? AuthSource = null
);

public record PruneBackupsRequest(
    int? StorageProfileId = null,
    int? Last = null,
    string? Within = null,
    int? Hourly = null,
    int? Daily = null,
    int? Weekly = null,
    int? Monthly = null,
    int? Yearly = null,
    bool DryRun = false
);

public record ArchiveBackupsRequest(
    int? SrcStorageProfileId = null,
    string? SrcPath = null,
    int? DstStorageProfileId = null,
    string? DstPath = null,
    int? Last = null,
    string? Within = null,
    bool DryRun = false
);

public record LicenseCheckRequest(string Feature);

public record LicenseInstallRequest(string TokenOrPath, bool System = false);

public record CustomCliRequest(
    string Subcommand,
    string[]? Args = null,
    string? ConfigPath = null,
    bool DryRun = false,
    string? LogLevel = null,
    string? LogFormat = null,
    bool Progress = true
);

public record UpdateYamlRequest(string YamlContent);

// Sidecar v2 JSON models matching nfrastack/db-backup retention.Sidecar
public class SidecarMetadataDto
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "dbbackup.sidecar";

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 2;

    [JsonPropertyName("tool")]
    public SidecarToolDto? Tool { get; set; }

    [JsonPropertyName("job")]
    public SidecarJobDto? Job { get; set; }

    [JsonPropertyName("trigger")]
    public string? Trigger { get; set; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("run_id")]
    public string? RunId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("raw_size")]
    public long RawSize { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("strategy")]
    public string? Strategy { get; set; }

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }

    [JsonPropertyName("schema_only")]
    public bool SchemaOnly { get; set; }

    [JsonPropertyName("tables")]
    public SidecarTablesDto? Tables { get; set; }

    [JsonPropertyName("notes")]
    public List<string>? Notes { get; set; }

    [JsonPropertyName("base")]
    public string? Base { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("db")]
    public string? Db { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("server")]
    public SidecarServerDto? Server { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("checksums")]
    public Dictionary<string, string>? Checksums { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("compress")]
    public string? Compress { get; set; }

    [JsonPropertyName("compression_level")]
    public int CompressionLevel { get; set; }

    [JsonPropertyName("compression_threads")]
    public int CompressionThreads { get; set; }

    [JsonPropertyName("rsyncable")]
    public bool Rsyncable { get; set; }

    [JsonPropertyName("chain_depth")]
    public int ChainDepth { get; set; }

    [JsonPropertyName("encryption")]
    public SidecarEncryptionDto? Encryption { get; set; }
}

public class SidecarToolDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("edition")]
    public string? Edition { get; set; }
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

[JsonConverter(typeof(SidecarJobJsonConverter))]
public class SidecarJobDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("schedule")]
    public string? Schedule { get; set; }
}

public class SidecarJobJsonConverter : JsonConverter<SidecarJobDto>
{
    public override SidecarJobDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new SidecarJobDto { Name = reader.GetString() };
        }
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var job = new SidecarJobDto();
            if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                job.Name = nameProp.GetString();
            }
            if (root.TryGetProperty("schedule", out var schedProp) && schedProp.ValueKind == JsonValueKind.String)
            {
                job.Schedule = schedProp.GetString();
            }
            return job;
        }
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, SidecarJobDto value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Name != null) writer.WriteString("name", value.Name);
        if (value.Schedule != null) writer.WriteString("schedule", value.Schedule);
        writer.WriteEndObject();
    }
}

public class SidecarTablesDto
{
    [JsonPropertyName("include")]
    public List<string>? Include { get; set; }
    [JsonPropertyName("exclude")]
    public List<string>? Exclude { get; set; }
    [JsonPropertyName("schema_only")]
    public List<string>? SchemaOnly { get; set; }
}

public class SidecarServerDto
{
    [JsonPropertyName("engine")]
    public string? Engine { get; set; }
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("arch")]
    public string? Arch { get; set; }
}

public class SidecarEncryptionDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("recipients")]
    public List<string>? Recipients { get; set; }
    [JsonPropertyName("passphrase")]
    public bool Passphrase { get; set; }
}

public class BackupArtifactDto
{
    public string Filename { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public int StorageProfileId { get; set; }
    public string StorageProfileName { get; set; } = string.Empty;
    public string StorageBackend { get; set; } = "filesystem";
    public string Type { get; set; } = string.Empty;
    public string Engine => Type;
    public string Database { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Strategy { get; set; } = "full";
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public long Size => SizeBytes;
    public long RawSizeBytes { get; set; }
    public long? RawSize => RawSizeBytes > 0 ? RawSizeBytes : null;
    public double CompressionRatio { get; set; }
    public string Compression { get; set; } = "none";
    public int CompressionLevel { get; set; }
    public string Encryption { get; set; } = "none";
    public bool SchemaOnly { get; set; }
    public bool HasSidecar { get; set; }
    public bool HasMd5File { get; set; }
    public bool HasSha1File { get; set; }
    public string? ChecksumAlgorithm { get; set; }
    public string? ChecksumValue { get; set; }
    public string ChecksumStatus => !string.IsNullOrEmpty(ChecksumValue)
        ? $"{ChecksumAlgorithm}:{ChecksumValue[..Math.Min(10, ChecksumValue.Length)]}"
        : (HasSidecar ? "sidecar-v2" : "none");
    public string? JobName { get; set; }
    public long DurationMs { get; set; }
    public string? ServerVersion { get; set; }
    public SidecarMetadataDto? Sidecar { get; set; }
}

public class DbbVersionInfoDto
{
    [JsonPropertyName("binary")]
    public string Binary { get; set; } = "db-backup";
    [JsonPropertyName("version")]
    public string Version { get; set; } = "5.1.0";
    [JsonPropertyName("commit")]
    public string Commit { get; set; } = "";
    [JsonPropertyName("build_date")]
    public string BuildDate { get; set; } = "";
    [JsonPropertyName("goos")]
    public string Goos { get; set; } = "linux";
    [JsonPropertyName("goarch")]
    public string Goarch { get; set; } = "amd64";
    [JsonPropertyName("edition")]
    public string Edition { get; set; } = "supported";
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "community";
    [JsonPropertyName("license")]
    public string License { get; set; } = "";
}

public class SupportedEngineDto
{
    public string Type { get; set; } = string.Empty;
    public string Engine => Type;
    public string[] Aliases { get; set; } = [];
    public string Label { get; set; } = string.Empty;
    public string DisplayName => Label;
    public int DefaultPort { get; set; }
    public string Extension { get; set; } = ".sql";
    public bool SupportsListDbs { get; set; }
    public bool SupportsSplitDb => SupportsListDbs;
    public bool SupportsSchemaOnly => Type is "mysql" or "mariadb" or "postgres" or "sqlite" or "mssql";
    public bool SupportsIncremental { get; set; }
    public string[] Strategies => SupportsIncremental ? ["full", "incremental", "differential"] : ["full"];
    public string[] MaintenanceOps { get; set; } = [];
}

public class SystemInfoDto
{
    public string BinaryPath { get; set; } = string.Empty;
    public bool BinaryAvailable { get; set; }
    public bool BinaryFound => BinaryAvailable;
    public DbbVersionInfoDto VersionInfo { get; set; } = new();
    public string Version => VersionInfo.Version;
    public string Edition => $"{VersionInfo.Edition} ({VersionInfo.Mode})";
    public string Platform => $"{VersionInfo.Goos}/{VersionInfo.Goarch}";
    public string BuildDate => VersionInfo.BuildDate;
    public string GitCommit => VersionInfo.Commit;
    public DbbVersionInfoDto RawVersionJson => VersionInfo;
    public string MaintenanceOpsOutput { get; set; } = string.Empty;
    public string MaintainOpsOutput => MaintenanceOpsOutput;
    public List<SupportedEngineDto> SupportedEngines { get; set; } = [];
    public string DataDir { get; set; } = string.Empty;
    public string BackupsDirectory => Path.Combine(DataDir, "backups");
    public string ConfigPath { get; set; } = string.Empty;
    public string ConfigFilePath => ConfigPath;
    public string DatabasePath { get; set; } = string.Empty;
    public string SampleDatabasePath { get; set; } = string.Empty;
    public string HangfireDashboardUrl { get; set; } = "/hangfire";
}

public class SchedulerJobDto
{
    public int JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsPaused { get; set; }
    public string? ScheduleCron { get; set; }
    public string? QuartzCron { get; set; }
    public int ScheduleIntervalMinutes { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? PreviousFireTime { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string LastStatus { get; set; } = "idle";
    public string TriggerState { get; set; } = "Normal";
    public string ConnectionName { get; set; } = string.Empty;
    public string EngineType { get; set; } = string.Empty;
    public string Strategy { get; set; } = "full";
}

public class DashboardSummaryDto
{
    public int TotalJobs { get; set; }
    public int ActiveJobs { get; set; }
    public int TotalConnections { get; set; }
    public int TotalStorageProfiles { get; set; }
    public int TotalEncryptionProfiles { get; set; }
    public int TotalNotificationProfiles { get; set; }
    public int TotalBackups { get; set; }
    public long TotalStorageBytes { get; set; }
    public long TotalRawBytes { get; set; }
    public int TotalExecutions { get; set; }
    public int CompletedExecutions { get; set; }
    public int SuccessfulExecutions => CompletedExecutions;
    public int FailedExecutions { get; set; }
    public int RunningExecutions { get; set; }
    public double SuccessRate { get; set; }
    public string BinaryVersion => SystemInfo.VersionInfo.Version;
    public string BinaryEdition => $"{SystemInfo.VersionInfo.Edition} ({SystemInfo.VersionInfo.Mode})";
    public List<JobExecution> RecentExecutions { get; set; } = [];
    public List<SchedulerJobDto> UpcomingSchedules { get; set; } = [];
    public List<SchedulerJobDto> UpcomingJobs => UpcomingSchedules;
    public List<BackupArtifactDto> RecentBackups { get; set; } = [];
    public SystemInfoDto SystemInfo { get; set; } = new();
}
