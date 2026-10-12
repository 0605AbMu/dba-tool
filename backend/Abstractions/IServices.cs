using DbaTool.Api.Models;

namespace DbaTool.Api.Abstractions;

public interface IDbaPathProvider
{
    string DataDir { get; }
    string BackupsDir { get; }
    string ConfigsDir { get; }
    string TempDir { get; }
    string StateDir { get; }
    string ConfigFilePath { get; }
    string SqliteDbPath { get; }
    void EnsureDirectories();
    string ResolveBinaryPath();
}

public interface IDbbConfigGeneratorService
{
    string DataDir { get; }
    string ConfigPath { get; }
    Task<string> GetYamlContentAsync(CancellationToken ct = default);
    Task<string> SaveYamlContentAsync(string yamlContent, CancellationToken ct = default);
    Task<string> RegenerateYamlAsync(CancellationToken ct = default);
}

public interface IDbbCliRunnerService
{
    string DataDir { get; }
    string ResolveBinaryPath();
    bool CancelExecution(int executionId);
    Task<JobExecution> RunJobBackupAsync(int jobId, bool dryRun, string trigger, CancellationToken ct = default);
    Task<JobExecution> RunJobMaintenanceAsync(int jobId, string trigger, CancellationToken ct = default);
    Task<JobExecution> VerifyBackupAsync(VerifyBackupRequest req, CancellationToken ct = default);
    Task<JobExecution> RestoreBackupAsync(RestoreBackupRequest req, CancellationToken ct = default);
    Task<JobExecution> PruneBackupsAsync(PruneBackupsRequest req, CancellationToken ct = default);
    Task<JobExecution> ArchiveBackupsAsync(ArchiveBackupsRequest req, CancellationToken ct = default);
    Task<JobExecution> ExecuteCustomCliAsync(CustomCliRequest req, CancellationToken ct = default);
    Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken ct = default);
    Task<object> CheckVersionAsync(CancellationToken ct = default);
    Task<object> GetLicenseInfoAsync(CancellationToken ct = default);
    Task<object> CheckLicenseFeatureAsync(string feature, CancellationToken ct = default);
    Task<object> InstallLicenseAsync(LicenseInstallRequest req, CancellationToken ct = default);
    Task<object> GetStatsAsync(CancellationToken ct = default);
    Task<object> TestConnectionAsync(ConnectionProfile conn, CancellationToken ct = default);
}

public interface IBackupStorageService
{
    string DataDir { get; }
    Task<List<BackupArtifactDto>> ListBackupsAsync(int? storageProfileId = null, string? prefix = null, CancellationToken ct = default);
    Task<SidecarMetadataDto?> GetSidecarAsync(string filename, int? storageProfileId = null, CancellationToken ct = default);
    Task<string?> GetBackupFilePathAsync(string filename, int? storageProfileId = null, CancellationToken ct = default);
    Task<bool> DeleteBackupAsync(string filename, int? storageProfileId = null, CancellationToken ct = default);
}

public interface IHangfireSchedulerService
{
    Task ExecuteScheduledBackupAsync(int jobId);
    Task SyncAllJobsAsync(CancellationToken ct = default);
    Task SyncJobAsync(int jobId, CancellationToken ct = default);
    void RemoveJob(int jobId);
    Task<List<SchedulerJobDto>> GetScheduledJobsAsync(CancellationToken ct = default);
    Task<SchedulerJobDto> PauseJobAsync(int jobId, CancellationToken ct = default);
    Task<SchedulerJobDto> ResumeJobAsync(int jobId, CancellationToken ct = default);
    Task<string> TriggerJobNowAsync(int jobId, CancellationToken ct = default);
}

public interface INotificationDispatcherService
{
    Task DispatchExecutionAlertAsync(JobExecution execution, CancellationToken ct = default);
    Task<bool> SendTestNotificationAsync(NotificationProfile profile, CancellationToken ct = default);
}
