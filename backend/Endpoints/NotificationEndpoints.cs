using DbaTool.Api.Abstractions;
using DbaTool.Api.Common;
using DbaTool.Api.Data;
using DbaTool.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DbaTool.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notification Profiles");

        group.MapGet("", GetNotificationsAsync);
        group.MapGet("/{id:int}", GetNotificationByIdAsync);
        group.MapPost("", CreateNotificationAsync);
        group.MapPut("/{id:int}", UpdateNotificationAsync);
        group.MapDelete("/{id:int}", DeleteNotificationAsync);
        group.MapPost("/{id:int}/test", TestNotificationAsync);

        return app;
    }

    private static async Task<IResult> GetNotificationsAsync(
        AppDbContext db,
        CancellationToken ct)
    {
        var list = await db.NotificationProfiles
            .AsNoTracking()
            .OrderBy(n => n.Id)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetNotificationByIdAsync(
        int id,
        AppDbContext db,
        CancellationToken ct)
    {
        var item = await db.NotificationProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new EntityNotFoundException("Notification profile", id);
        return Results.Ok(item);
    }

    private static async Task<IResult> CreateNotificationAsync(
        NotificationProfile input,
        AppDbContext db,
        CancellationToken ct)
    {
        input.Id = 0;
        input.Name = SlugHelper.Sanitize(input.Name, "alert");
        input.CreatedAt = DateTime.UtcNow;

        db.NotificationProfiles.Add(input);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/notifications/{input.Id}", input);
    }

    private static async Task<IResult> UpdateNotificationAsync(
        int id,
        NotificationProfile input,
        AppDbContext db,
        CancellationToken ct)
    {
        var existing = await db.NotificationProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Notification profile", id);

        existing.Name = SlugHelper.Sanitize(input.Name, existing.Name);
        existing.Channel = input.Channel;
        existing.IsEnabled = input.IsEnabled;
        existing.NotifyOnSuccess = input.NotifyOnSuccess;
        existing.NotifyOnFailure = input.NotifyOnFailure;
        existing.BotToken = input.BotToken;
        existing.ChatId = input.ChatId;
        existing.WebhookUrl = input.WebhookUrl;
        existing.SmtpHost = input.SmtpHost;
        existing.SmtpPort = input.SmtpPort;
        existing.SmtpUsername = input.SmtpUsername;
        existing.SmtpPassword = input.SmtpPassword;
        existing.SmtpUseSsl = input.SmtpUseSsl;
        existing.EmailFrom = input.EmailFrom;
        existing.EmailTo = input.EmailTo;

        await db.SaveChangesAsync(ct);
        return Results.Ok(existing);
    }

    private static async Task<IResult> DeleteNotificationAsync(
        int id,
        AppDbContext db,
        CancellationToken ct)
    {
        var existing = await db.NotificationProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Notification profile", id);

        db.NotificationProfiles.Remove(existing);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { deleted = true, id });
    }

    private static async Task<IResult> TestNotificationAsync(
        int id,
        AppDbContext db,
        INotificationDispatcherService dispatcher,
        CancellationToken ct)
    {
        var existing = await db.NotificationProfiles.FindAsync([id], ct)
            ?? throw new EntityNotFoundException("Notification profile", id);

        try
        {
            var success = await dispatcher.SendTestNotificationAsync(existing, ct);
            return Results.Ok(new
            {
                success,
                message = success
                    ? $"Test notification sent successfully via {existing.Channel}."
                    : $"Failed to deliver test notification via {existing.Channel}."
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                success = false,
                message = $"Test notification failed: {ex.Message}"
            });
        }
    }
}
