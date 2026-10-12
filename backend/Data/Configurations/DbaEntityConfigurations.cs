using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DbaTool.Api.Data.Configurations;

public sealed class ConnectionProfileConfiguration : IEntityTypeConfiguration<ConnectionProfile>
{
    public void Configure(EntityTypeBuilder<ConnectionProfile> builder)
    {
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

public sealed class StorageProfileConfiguration : IEntityTypeConfiguration<StorageProfile>
{
    public void Configure(EntityTypeBuilder<StorageProfile> builder)
    {
        builder.HasIndex(s => s.Name).IsUnique();
    }
}

public sealed class EncryptionProfileConfiguration : IEntityTypeConfiguration<EncryptionProfile>
{
    public void Configure(EntityTypeBuilder<EncryptionProfile> builder)
    {
        builder.HasIndex(e => e.Name).IsUnique();
    }
}

public sealed class BackupJobConfiguration : IEntityTypeConfiguration<BackupJob>
{
    public void Configure(EntityTypeBuilder<BackupJob> builder)
    {
        builder.HasIndex(j => j.Name).IsUnique();

        builder.HasOne(j => j.ConnectionProfile)
            .WithMany()
            .HasForeignKey(j => j.ConnectionProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(j => j.StorageProfile)
            .WithMany()
            .HasForeignKey(j => j.StorageProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(j => j.EncryptionProfile)
            .WithMany()
            .HasForeignKey(j => j.EncryptionProfileId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class JobExecutionConfiguration : IEntityTypeConfiguration<JobExecution>
{
    public void Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.HasIndex(e => e.StartedAt);
    }
}

public sealed class NotificationProfileConfiguration : IEntityTypeConfiguration<NotificationProfile>
{
    public void Configure(EntityTypeBuilder<NotificationProfile> builder)
    {
        builder.HasIndex(n => n.Name).IsUnique();
    }
}
