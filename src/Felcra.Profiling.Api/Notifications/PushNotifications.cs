using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Felcra.Profiling.Api.Breeding;
using Felcra.Profiling.Contracts.Auth;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Domain.Notifications;
using Felcra.Profiling.Infrastructure.Persistence;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PushSubscription = Felcra.Profiling.Domain.Notifications.PushSubscription;

namespace Felcra.Profiling.Api.Notifications;

/// <summary>Web Push (VAPID) settings. Generate a key pair with <c>dotnet Felcra.Profiling.Api.dll vapid-keys</c>.</summary>
public sealed class PushOptions
{
    public const string Section = "Push";

    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }

    /// <summary>Contact for push services, e.g. "mailto:it@felcra.com.my".</summary>
    public string Subject { get; set; } = "mailto:admin@felcra.local";

    /// <summary>Hour (Malaysia time, 0–23) from which each day's task notification is sent.</summary>
    public int Hour { get; set; } = 7;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);

    /// <summary>A new P-256 key pair, base64url encoded as browsers expect.</summary>
    public static (string PublicKey, string PrivateKey) GenerateKeys()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ecdsa.ExportParameters(includePrivateParameters: true);
        return (Base64Url([0x04, .. p.Q.X!, .. p.Q.Y!]), Base64Url(p.D!));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>What the service worker shows: title, text and the page opened on tap.</summary>
internal sealed record PushPayload(string Title, string Body, string Url, string Tag = "tasks");

internal enum PushOutcome
{
    Sent,
    /// <summary>The device unsubscribed or the subscription expired: forget it.</summary>
    Gone,
    Failed,
}

internal interface IPushSender
{
    bool IsConfigured { get; }
    Task<PushOutcome> SendAsync(PushSubscription subscription, PushPayload payload, CancellationToken ct);
}

internal sealed class WebPushSender(IHttpClientFactory httpFactory, IOptions<PushOptions> options, ILogger<WebPushSender> logger) : IPushSender, IDisposable
{
    private readonly PushOptions _options = options.Value;
    private readonly Lazy<VapidAuthentication> _vapid = new(() =>
        new VapidAuthentication(options.Value.PublicKey!, options.Value.PrivateKey!) { Subject = options.Value.Subject });

    public bool IsConfigured => _options.IsConfigured;

    public async Task<PushOutcome> SendAsync(PushSubscription subscription, PushPayload payload, CancellationToken ct)
    {
        var target = new Lib.Net.Http.WebPush.PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);
        var message = new PushMessage(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web))
        {
            Topic = payload.Tag,
            TimeToLive = (int)TimeSpan.FromHours(12).TotalSeconds,
            Urgency = PushMessageUrgency.Normal,
        };

        try
        {
            var client = new PushServiceClient(httpFactory.CreateClient("webpush")) { DefaultAuthentication = _vapid.Value };
            await client.RequestPushMessageDeliveryAsync(target, message, ct);
            return PushOutcome.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushOutcome.Gone;
        }
        catch (Exception ex) when (ex is PushServiceClientException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Push to {Host} failed", new Uri(subscription.Endpoint).Host);
            return PushOutcome.Failed;
        }
    }

    public void Dispose()
    {
        if (_vapid.IsValueCreated) _vapid.Value.Dispose();
    }
}

/// <summary>
/// Each morning, tells field officers and farm managers how many breeding and treatment tasks are due
/// today or overdue on their farms (the same tasks as the field app's list).
/// </summary>
internal sealed class ReminderNotificationJob(
    AppDbContext db, BreedingQueries breeding, IPushSender push, TimeProvider clock, ILogger<ReminderNotificationJob> logger)
{
    /// <summary>Dated tasks only; low body condition is a standing flag and would repeat every day.</summary>
    private static readonly ReminderType[] Notified = [ReminderType.PregnancyCheckDue, ReminderType.CalvingDue, ReminderType.TreatmentDue];

    /// <summary>Sends a day's notifications once. Returns false when that day was already done.</summary>
    public async Task<bool> RunDailyAsync(DateOnly date, CancellationToken ct)
    {
        if (await db.ReminderNotificationRuns.AnyAsync(r => r.Date == date, ct)) return false;
        var run = new ReminderNotificationRun { Date = date, StartedAt = clock.GetUtcNow() };
        db.ReminderNotificationRuns.Add(run);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return false; // Another instance claimed the day.
        }

        try
        {
            run.NotificationsSent = await SendTasksAsync(ct);
            run.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Release the claim so the next check tries again.
            db.ChangeTracker.Clear();
            db.ReminderNotificationRuns.Remove(await db.ReminderNotificationRuns.SingleAsync(r => r.Id == run.Id, CancellationToken.None));
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        logger.LogInformation("Task notifications for {Date}: {Count} sent", date, run.NotificationsSent);
        return true;
    }

    private async Task<int> SendTasksAsync(CancellationToken ct)
    {
        var subscribers = await (
                from s in db.PushSubscriptions
                join u in db.Users on s.UserId equals u.Id
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where u.IsActive && (r.Name == Roles.FieldOfficer || r.Name == Roles.FarmManager)
                select s.UserId)
            .Distinct().ToListAsync(ct);
        if (subscribers.Count == 0) return 0;

        var farmsByUser = (await db.UserFarms.AsNoTracking().Where(uf => subscribers.Contains(uf.UserId)).ToListAsync(ct))
            .GroupBy(uf => uf.UserId).ToDictionary(g => g.Key, g => g.Select(uf => uf.FarmId).ToHashSet());
        var today = breeding.Today;
        var tasks = (await breeding.RemindersAsync(null, 0, ct)).Where(r => Notified.Contains(r.Type)).ToList();
        var languages = await db.Users.Where(u => subscribers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Language, ct);

        var sent = 0;
        foreach (var userId in subscribers)
        {
            var farms = farmsByUser.GetValueOrDefault(userId) ?? [];
            var mine = tasks.Where(t => farms.Contains(t.FarmId)).ToList();
            if (mine.Count == 0) continue;
            using var language = Use(Parse(languages.GetValueOrDefault(userId)) ?? DefaultLanguage);
            sent += await SendToUserAsync(userId, Summarise(mine, today), ct);
        }
        return sent;
    }

    /// <summary>The morning message, in the current language.</summary>
    internal static PushPayload Summarise(IReadOnlyList<ReminderDto> tasks, DateOnly today)
    {
        var parts = tasks.GroupBy(t => t.Type).OrderBy(g => g.Key).Select(g => g.Key switch
        {
            ReminderType.PregnancyCheckDue => T(g.Count() == 1 ? "{0} pregnancy check" : "{0} pregnancy checks", g.Count()),
            ReminderType.CalvingDue => T(g.Count() == 1 ? "{0} calving due" : "{0} calvings due", g.Count()),
            _ => T(g.Count() == 1 ? "{0} treatment due" : "{0} treatments due", g.Count()),
        });
        var overdue = tasks.Count(t => t.IsOverdueOn(today));
        return new PushPayload(
            T(tasks.Count == 1 ? "{0} task today" : "{0} tasks today", tasks.Count),
            string.Join(", ", parts) + (overdue > 0 ? " · " + T("{0} overdue", overdue) : ""),

            "/field/");
    }

    /// <summary>Sends to every device of the user; forgets devices the push service no longer knows.</summary>
    public async Task<int> SendToUserAsync(Guid userId, PushPayload payload, CancellationToken ct)
    {
        var sent = 0;
        foreach (var subscription in await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct))
        {
            switch (await push.SendAsync(subscription, payload, ct))
            {
                case PushOutcome.Sent:
                    subscription.LastSentAt = clock.GetUtcNow();
                    sent++;
                    break;
                case PushOutcome.Gone:
                    db.PushSubscriptions.Remove(subscription);
                    break;
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
    }
}

/// <summary>Checks every 15 minutes whether today's task notifications are due and sends them once.</summary>
internal sealed class ReminderNotificationService(
    IServiceScopeFactory scopes, IOptions<PushOptions> options, TimeProvider clock, ILogger<ReminderNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15), clock);
        do
        {
            try
            {
                var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(8));
                if (now.Hour >= options.Value.Hour)
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ReminderNotificationJob>().RunDailyAsync(DateOnly.FromDateTime(now.DateTime), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Task notifications failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
