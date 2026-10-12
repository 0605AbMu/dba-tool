using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Models;

namespace DbaTool.Api.Endpoints;

public static class BackupArtifactEndpoints
{
    public static IEndpointRouteBuilder MapBackupArtifactEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backups").WithTags("Backup Artifacts & Operations");

        group.MapGet("", ListBackupsAsync);
        group.MapGet("/{filename}/sidecar", GetSidecarAsync);
        group.MapGet("/{filename}/download", DownloadBackupAsync);
        group.MapDelete("/{filename}", DeleteBackupAsync);
        group.MapPost("/verify", VerifyBackupAsync);
        group.MapPost("/restore", RestoreBackupAsync);
        group.MapPost("/prune", PruneBackupsAsync);
        group.MapPost("/archive", ArchiveBackupsAsync);

        return app;
    }

    private static async Task<IResult> ListBackupsAsync(
        int? storageProfileId,
        string? prefix,
        IBackupStorageService storageSvc,
        CancellationToken ct)
    {
        var list = await storageSvc.ListBackupsAsync(storageProfileId, prefix, ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetSidecarAsync(
        string filename,
        int? storageProfileId,
        IBackupStorageService storageSvc,
        CancellationToken ct)
    {
        var sidecar = await storageSvc.GetSidecarAsync(filename, storageProfileId, ct)
            ?? throw new EntityNotFoundException($"Sidecar metadata (.json) not found for '{filename}'.");

        return Results.Ok(sidecar);
    }

    private static async Task<IResult> DownloadBackupAsync(
        string filename,
        int? storageProfileId,
        IBackupStorageService storageSvc,
        CancellationToken ct)
    {
        var path = await storageSvc.GetBackupFilePathAsync(filename, storageProfileId, ct)
            ?? throw new EntityNotFoundException($"Backup file '{filename}' not found.");

        return Results.File(path, "application/octet-stream", Path.GetFileName(path));
    }

    private static async Task<IResult> DeleteBackupAsync(
        string filename,
        int? storageProfileId,
        IBackupStorageService storageSvc,
        CancellationToken ct)
    {
        var deleted = await storageSvc.DeleteBackupAsync(filename, storageProfileId, ct);
        if (!deleted)
        {
            throw new EntityNotFoundException($"Backup file '{filename}' not found.");
        }

        return Results.Ok(new { deleted = true, filename });
    }

    private static async Task<IResult> VerifyBackupAsync(
        VerifyBackupRequest req,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var exec = await cliRunner.VerifyBackupAsync(req, ct);
        return Results.Ok(exec);
    }

    private static async Task<IResult> RestoreBackupAsync(
        RestoreBackupRequest req,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var exec = await cliRunner.RestoreBackupAsync(req, ct);
        return Results.Ok(exec);
    }

    private static async Task<IResult> PruneBackupsAsync(
        PruneBackupsRequest req,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var exec = await cliRunner.PruneBackupsAsync(req, ct);
        return Results.Ok(exec);
    }

    private static async Task<IResult> ArchiveBackupsAsync(
        ArchiveBackupsRequest req,
        IDbbCliRunnerService cliRunner,
        CancellationToken ct)
    {
        var exec = await cliRunner.ArchiveBackupsAsync(req, ct);
        return Results.Ok(exec);
    }
}
