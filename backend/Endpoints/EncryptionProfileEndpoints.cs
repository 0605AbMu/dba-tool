using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Endpoints;

public static class EncryptionProfileEndpoints
{
    public static IEndpointRouteBuilder MapEncryptionProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/encryption-profiles").WithTags("Encryption Profiles");

        group.MapGet("", GetEncryptionProfilesAsync);
        group.MapPost("", CreateEncryptionProfileAsync);
        group.MapPut("/{id:int}", UpdateEncryptionProfileAsync);
        group.MapDelete("/{id:int}", DeleteEncryptionProfileAsync);

        return app;
    }

    private static async Task<IResult> GetEncryptionProfilesAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        var list = await db.EncryptionProfiles
            .AsNoTracking()
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateEncryptionProfileAsync(
        EncryptionProfile input,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        input.Id = 0;
        input.Name = SlugHelper.Sanitize(input.Name, "encryption");
        input.CreatedAt = DateTime.UtcNow;

        db.EncryptionProfiles.Add(input);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Created($"/api/encryption-profiles/{input.Id}", input);
    }

    private static async Task<IResult> UpdateEncryptionProfileAsync(
        int id,
        EncryptionProfile input,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var existing = await db.EncryptionProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Encryption profile", id);

        existing.Name = SlugHelper.Sanitize(input.Name, existing.Name);
        existing.Type = input.Type;
        existing.Recipients = input.Recipients;
        existing.Passphrase = input.Passphrase;

        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteEncryptionProfileAsync(
        int id,
        AppDbContext db,
        IDbbConfigGeneratorService cfg,
        CancellationToken ct)
    {
        var existing = await db.EncryptionProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Encryption profile", id);

        db.EncryptionProfiles.Remove(existing);
        await db.SaveChangesAsync(ct);
        await cfg.RegenerateYamlAsync(ct);

        return Results.Ok(new { deleted = true, id });
    }
}
