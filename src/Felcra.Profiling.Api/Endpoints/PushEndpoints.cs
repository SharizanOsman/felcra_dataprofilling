using Felcra.Profiling.Api.Infrastructure;
using Felcra.Profiling.Api.Notifications;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Contracts.Notifications;
using Felcra.Profiling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PushSubscription = Felcra.Profiling.Domain.Notifications.PushSubscription;

namespace Felcra.Profiling.Api.Endpoints;

/// <summary>Web Push subscriptions for the signed-in user's devices.</summary>
internal static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push").WithTags("Notifications");
        group.MapGet("/config", (IOptions<PushOptions> options) =>
            TypedResults.Ok(new PushConfigDto(options.Value.IsConfigured ? options.Value.PublicKey : null)));
        group.MapPost("/subscriptions", Subscribe).Validate<PushSubscriptionRequest>();
        group.MapDelete("/subscriptions", Unsubscribe);
        group.MapPost("/test", SendTest);
        return app;
    }

    /// <summary>Adds the device, or moves it to this user (a shared phone signed in by someone else).</summary>
    private static async Task<NoContent> Subscribe(
        PushSubscriptionRequest request, AppDbContext db, ICurrentUser user, TimeProvider clock, HttpContext http, CancellationToken ct)
    {
        var existing = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = user.UserId!.Value, Endpoint = request.Endpoint, P256dh = request.P256dh, Auth = request.Auth,
                UserAgent = Truncate(http.Request.Headers.UserAgent.ToString(), 300), CreatedAt = clock.GetUtcNow(),
            });
        }
        else
        {
            (existing.UserId, existing.P256dh, existing.Auth) = (user.UserId!.Value, request.P256dh, request.Auth);
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Removes one of the user's own devices; nothing happens for unknown or other users' endpoints.</summary>
    private static async Task<NoContent> Unsubscribe(string endpoint, AppDbContext db, ICurrentUser user, CancellationToken ct)
    {
        await db.PushSubscriptions.Where(s => s.Endpoint == endpoint && s.UserId == user.UserId).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PushTestResultDto>, ValidationProblem>> SendTest(
        ReminderNotificationJob job, IPushSender push, ICurrentUser user, CancellationToken ct)
    {
        if (!push.IsConfigured)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Push"] = [T("Push notifications are not set up on the server.")] });
        var sent = await job.SendToUserAsync(user.UserId!.Value,
            new PushPayload(T("Notifications are on"), T("You will get a message each morning when tasks are due."), "/field/", "test"), ct);
        return TypedResults.Ok(new PushTestResultDto(sent));
    }

    private static string? Truncate(string value, int length) => value.Length == 0 ? null : value.Length <= length ? value : value[..length];
}
