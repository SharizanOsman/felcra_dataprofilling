using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Api.Notifications;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Notifications;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PushTests(ApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    private static PushSubscriptionRequest Device(string name) =>
        new() { Endpoint = $"https://push.example/{name}-{Guid.NewGuid():N}", P256dh = "BPublicKeyOfTheDevice", Auth = "authSecret" };

    private async Task<bool> SubscriptionExistsAsync(string endpoint, Guid? userId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = factory.CreateDbContext(scope);
        return await db.PushSubscriptions.AnyAsync(s => s.Endpoint == endpoint && (userId == null || s.UserId == userId));
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        return await factory.CreateDbContext(scope).Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
    }

    [Fact]
    public async Task Config_exposes_only_the_public_key()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var config = await officer.GetFromJsonAsync<PushConfigDto>("/api/push/config");
        Assert.False(string.IsNullOrEmpty(config!.PublicKey));
        Assert.Equal(87, config.PublicKey!.Length); // 65-byte uncompressed P-256 point, base64url
    }

    [Fact]
    public async Task Devices_subscribe_move_between_users_and_unsubscribe()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var phone = Device("shared");

        Assert.Equal(HttpStatusCode.NoContent, (await manager.PostAsJsonAsync("/api/push/subscriptions", phone)).StatusCode);
        Assert.True(await SubscriptionExistsAsync(phone.Endpoint, await UserIdAsync(ApiFactory.ManagerEmail)));

        // The same phone signed in by the officer now belongs to the officer.
        (await officer.PostAsJsonAsync("/api/push/subscriptions", phone)).EnsureSuccessStatusCode();
        Assert.True(await SubscriptionExistsAsync(phone.Endpoint, await UserIdAsync(ApiFactory.OfficerEmail)));

        // Only the owner can remove it.
        await manager.DeleteAsync($"/api/push/subscriptions?endpoint={Uri.EscapeDataString(phone.Endpoint)}");
        Assert.True(await SubscriptionExistsAsync(phone.Endpoint));
        await officer.DeleteAsync($"/api/push/subscriptions?endpoint={Uri.EscapeDataString(phone.Endpoint)}");
        Assert.False(await SubscriptionExistsAsync(phone.Endpoint));
    }

    [Fact]
    public async Task Only_https_push_endpoints_are_accepted()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        foreach (var endpoint in new[] { "http://push.example/x", "file:///etc/passwd", "not a url" })
        {
            var response = await officer.PostAsJsonAsync("/api/push/subscriptions", new PushSubscriptionRequest { Endpoint = endpoint, P256dh = "k", Auth = "a" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Test_notification_goes_to_the_users_devices()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var phone = Device("test");
        (await officer.PostAsJsonAsync("/api/push/subscriptions", phone)).EnsureSuccessStatusCode();

        var result = await (await officer.PostAsync("/api/push/test", null)).ReadAsync<PushTestResultDto>();

        Assert.True(result.Sent >= 1);
        Assert.Equal("test", Assert.Single(factory.Push.To(phone.Endpoint)).Tag);
    }

    [Fact]
    public async Task Morning_notification_summarises_tasks_on_the_officers_farm_once_a_day()
    {
        // The officer uses the field app in Malay, so the morning message is in Malay.
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail, "ms");
        var farm = (await officer.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single();
        var dam = await (await officer.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farm.Id, TagNumber = TestHelpers.UniqueCode("p"), Category = AnimalCategory.Dam, Sex = Sex.Female,
        })).ReadAsync<AnimalDto>();
        // Served 45 days ago: the pregnancy check is due today.
        (await officer.PostAsJsonAsync($"/api/animals/{dam.Id}/breeding-events", new BreedingEventRequest
        {
            Type = BreedingEventType.Insemination, EventDate = Today.AddDays(-45),
        })).EnsureSuccessStatusCode();

        var phone = Device("officer");
        var expired = Device("gone");
        (await officer.PostAsJsonAsync("/api/push/subscriptions", phone)).EnsureSuccessStatusCode();
        (await officer.PostAsJsonAsync("/api/push/subscriptions", expired)).EnsureSuccessStatusCode();
        // HQ has no farm tasks: a subscribed admin gets nothing.
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var hqPhone = Device("hq");
        (await hq.PostAsJsonAsync("/api/push/subscriptions", hqPhone)).EnsureSuccessStatusCode();

        var day = new DateOnly(2002, 2, 2);
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<ReminderNotificationJob>();
            Assert.True(await job.RunDailyAsync(day, CancellationToken.None));
            Assert.False(await job.RunDailyAsync(day, CancellationToken.None));
        }

        var message = Assert.Single(factory.Push.To(phone.Endpoint));
        Assert.Matches(@"^\d+ tugasan hari ini$", message.Title);
        Assert.Contains("pemeriksaan kebuntingan", message.Body);

        Assert.Equal("/field/", message.Url);
        Assert.Empty(factory.Push.To(hqPhone.Endpoint));
        Assert.False(await SubscriptionExistsAsync(expired.Endpoint)); // The push service said it is gone.
    }

    [Fact]
    public void Summary_counts_each_kind_and_overdue_tasks()
    {
        var today = new DateOnly(2026, 10, 11);
        ReminderDto Task(ReminderType type, int daysAgo) =>
            new(type, Guid.NewGuid(), "A000001", "1", Guid.NewGuid(), "PAGOH", today.AddDays(-daysAgo), "", null);

        var payload = ReminderNotificationJob.Summarise(
            [Task(ReminderType.PregnancyCheckDue, 0), Task(ReminderType.PregnancyCheckDue, 3), Task(ReminderType.CalvingDue, 0), Task(ReminderType.TreatmentDue, 0)], today);

        Assert.Equal("4 tasks today", payload.Title);
        Assert.Equal("2 pregnancy checks, 1 calving due, 1 treatment due · 1 overdue", payload.Body);
    }
}
