using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ConnectionProfile> ConnectionProfiles => Set<ConnectionProfile>();
    public DbSet<StorageProfile> StorageProfiles => Set<StorageProfile>();
    public DbSet<EncryptionProfile> EncryptionProfiles => Set<EncryptionProfile>();
    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();
    public DbSet<JobExecution> JobExecutions => Set<JobExecution>();
    public DbSet<NotificationProfile> NotificationProfiles => Set<NotificationProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
