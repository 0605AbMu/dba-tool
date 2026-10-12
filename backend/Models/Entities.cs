using System.ComponentModel.DataAnnotations;

namespace DbaTool.Api.Models;

public class ConnectionProfile
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Type { get; set; } = "postgres"; // mysql|mariadb|postgres|mongo|mssql|redis|sqlite|influx|couch
    [Required]
    public string Host { get; set; } = "localhost"; // for sqlite, file path
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; } // supports literal, env://, file://
    public string? AuthSource { get; set; }
    public int InfluxVersion { get; set; }
    public string? InfluxMode { get; set; }
    public bool TlsEnabled { get; set; }
    public string? TlsCaFile { get; set; }
    public string? TlsCertFile { get; set; }
    public string? TlsKeyFile { get; set; }
    public bool TlsVerify { get; set; } = true;
    public string? TlsVersion { get; set; } // "1.2" | "1.3"
    public string ConnectivityMethod { get; set; } = "full"; // full|simple|none
    public int RetryIntervalSeconds { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 30;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class StorageProfile
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Backend { get; set; } = "filesystem"; // filesystem|s3|webdav|azure|gcs
    public string Path { get; set; } = string.Empty;
    public string? Bucket { get; set; }
    public string? Region { get; set; }
    public string? Endpoint { get; set; }
    public string? KeyId { get; set; }
    public string? KeySecret { get; set; }
    public string? Account { get; set; }
    public string? Key { get; set; }
    public string? WebDavUrl { get; set; }
    public string? WebDavUser { get; set; }
    public string? WebDavPass { get; set; }
    public string FileMode { get; set; } = "600";
    public string DirMode { get; set; } = "700";
    public string? OwnerUser { get; set; }
    public string? OwnerGroup { get; set; }
    public bool TlsEnabled { get; set; }
    public string? TlsCaFile { get; set; }
    public string? TlsCertFile { get; set; }
    public string? TlsKeyFile { get; set; }
    public bool TlsVerify { get; set; } = true;
    public string? TlsVersion { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EncryptionProfile
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Type { get; set; } = "none"; // none|age|gpg|openssl
    public string? Recipients { get; set; } // comma-separated public keys
    public string? Passphrase { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BackupJob
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;

    public int ConnectionProfileId { get; set; }
    public ConnectionProfile? ConnectionProfile { get; set; }

    public string DatabaseNames { get; set; } = "ALL"; // comma-separated, ALL, or __globals__; for sqlite main
    public string? ExcludeDatabases { get; set; }
    public bool SplitDb { get; set; } = true;

    public string? TablesInclude { get; set; }
    public string? TablesExclude { get; set; }
    public string? TablesSchemaOnly { get; set; }
    public bool SchemaOnly { get; set; }

    public bool MySqlRoutines { get; set; } = true;
    public bool MySqlEvents { get; set; } = true;
    public bool MySqlTriggers { get; set; } = true;
    public bool MySqlViews { get; set; } = true;
    public bool MySqlRawBlobs { get; set; }
    public bool PostgresGlobals { get; set; }

    public string Strategy { get; set; } = "full"; // full|incremental|differential
    public string FilenameTemplate { get; set; } = "%type%-%db%-%host%-%strategy%-%timestamp%";
    public bool CreateLatest { get; set; }
    public int FullEvery { get; set; }
    public string? FullAfter { get; set; }

    public string CompressionType { get; set; } = "zstd"; // zstd|gz|bz|xz|none
    public int CompressionLevel { get; set; } = 3;
    public int CompressionThreads { get; set; } = 1;
    public bool CompressionRsyncable { get; set; }

    public string Checksum { get; set; } = "md5"; // md5|sha1|none

    public int StorageProfileId { get; set; }
    public StorageProfile? StorageProfile { get; set; }

    public int? EncryptionProfileId { get; set; }
    public EncryptionProfile? EncryptionProfile { get; set; }

    // Post-Backup Archive (archive: block)
    public bool ArchiveEnabled { get; set; }
    public int ArchiveLast { get; set; }
    public string? ArchiveWithin { get; set; }
    public string? ArchivePath { get; set; }
    public int? ArchiveStorageProfileId { get; set; }
    public string? ArchiveRetentionWithin { get; set; }

    // Schedule & Advanced Blackout Window
    public string? ScheduleCron { get; set; } // 5-field cron like "0 2 * * *" or empty
    public int ScheduleIntervalMinutes { get; set; }
    public string? ScheduleTime { get; set; } // "02:00, 14:00"
    public string? ScheduleBegin { get; set; } // "+5"
    public string? ScheduleStart { get; set; } // "+10"
    public string? ScheduleRecurring { get; set; } // "0 15 * * *"
    public string? ScheduleDays { get; set; } // "mon,wed,fri" or "weekdays"
    public string? ScheduleExcludeDays { get; set; } // "sat,sun"
    public string? BlackoutBegin { get; set; } // "09:00"
    public string? BlackoutEnd { get; set; } // "17:00"
    public string? BlackoutDays { get; set; } // "mon,tue,wed,thu,fri"

    // Retention
    public int RetentionLast { get; set; } = 5;
    public string? RetentionWithin { get; set; }
    public int RetentionHourly { get; set; }
    public int RetentionDaily { get; set; } = 7;
    public int RetentionWeekly { get; set; } = 4;
    public int RetentionMonthly { get; set; } = 6;
    public int RetentionYearly { get; set; } = 2;

    // Maintenance
    public bool MaintCheckTables { get; set; }
    public bool MaintOptimize { get; set; }
    public bool MaintVacuum { get; set; }
    public bool MaintReindex { get; set; }
    public bool MaintAnalyze { get; set; }
    public bool MaintCompact { get; set; }
    public bool MaintMemoryPurge { get; set; }
    public string? MaintenanceScheduleCron { get; set; }
    public bool StandaloneMaintenanceOnly { get; set; }

    public string? PreHooks { get; set; }
    public string? PostHooks { get; set; }

    // Per-Job Logging Overrides
    public string LogLevel { get; set; } = "info";
    public string? LogPath { get; set; }
    public bool ProgressEnabled { get; set; } = true;
    public bool TimingsEnabled { get; set; } = true;
    public bool ColourEnabled { get; set; }
    public string SizeFormat { get; set; } = "bytes";

    public DateTime? LastRunAt { get; set; }
    public string LastStatus { get; set; } = "idle"; // idle|running|completed|failed
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class JobExecution
{
    public int Id { get; set; }
    public int? JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string CommandType { get; set; } = "dump"; // dump|restore|verify|prune|maintain|archive|list|version|license|raw
    public string Trigger { get; set; } = "manual"; // manual|scheduled|cli
    public string CliCommand { get; set; } = string.Empty;
    public string Status { get; set; } = "running"; // running|completed|failed|cancelled
    public int? ExitCode { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? OutputFile { get; set; }
    public long? OutputSize { get; set; }
    public bool IsDryRun { get; set; }
    public string LogOutput { get; set; } = string.Empty;
    public string? CurrentStage { get; set; } // INIT|CONNECT|EXTRACT|COMPRESS|ENCRYPT|STORAGE|RETENTION|NOTIFY|COMPLETED|FAILED
    public string? ErrorSummary { get; set; }
    public string? ErrorDetails { get; set; }
}

public class NotificationProfile
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Channel { get; set; } = "telegram"; // telegram|slack|discord|webhook|email
    public bool IsEnabled { get; set; } = true;
    public bool NotifyOnSuccess { get; set; } = true;
    public bool NotifyOnFailure { get; set; } = true;
    public string? BotToken { get; set; }
    public string? ChatId { get; set; }
    public string? WebhookUrl { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpUseSsl { get; set; } = true;
    public string? EmailFrom { get; set; }
    public string? EmailTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
