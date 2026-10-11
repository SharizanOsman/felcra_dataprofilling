using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Contracts.Sync;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Records;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SyncTests(ApiFactory factory)
{
    private static string Since(DateTimeOffset time) => Uri.EscapeDataString(time.ToString("O"));

    private async Task<(HttpClient Manager, HttpClient Officer, Guid FarmId)> ClientsAsync()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var farmId = (await manager.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single().Id;
        return (manager, await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail), farmId);
    }

    private static async Task<AnimalDto> NewAnimalAsync(HttpClient client, Guid farmId) =>
        await (await client.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farmId, TagNumber = TestHelpers.UniqueCode("s"), Category = AnimalCategory.Dam, Sex = Sex.Female,
        })).ReadAsync<AnimalDto>();

    [Fact]
    public async Task Full_pull_contains_only_the_users_farms_and_reference_lists()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var other = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("o"), Name = "Other" })).ReadAsync<FarmDto>();
        var hidden = await NewAnimalAsync(hq, other.Id);
        var (_, officer, farmId) = await ClientsAsync();
        var visible = await NewAnimalAsync(officer, farmId);

        var pull = await officer.GetFromJsonAsync<SyncPullDto>("/api/sync/pull");

        Assert.True(pull!.IsFull);
        Assert.Contains(pull.Animals, a => a.Id == visible.Id);
        Assert.DoesNotContain(pull.Animals, a => a.Id == hidden.Id);
        Assert.All(pull.Animals, a => Assert.Equal(farmId, a.FarmId));
        Assert.NotEmpty(pull.TreatmentProducts);
    }

    [Fact]
    public async Task Delta_pull_includes_animals_whose_latest_weight_changed()
    {
        var (_, officer, farmId) = await ClientsAsync();
        var animal = await NewAnimalAsync(officer, farmId);
        var first = await officer.GetFromJsonAsync<SyncPullDto>("/api/sync/pull");

        (await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements",
            new MeasurementRequest { Type = MeasurementType.Weight, Value = 401, MeasuredOn = new DateOnly(2026, 10, 2) })).EnsureSuccessStatusCode();
        (await officer.PostAsJsonAsync($"/api/animals/{animal.Id}/measurements",
            new MeasurementRequest { Type = MeasurementType.BodyCondition, Value = 3.5m, MeasuredOn = new DateOnly(2026, 10, 2) })).EnsureSuccessStatusCode();

        var delta = await officer.GetFromJsonAsync<SyncPullDto>($"/api/sync/pull?since={Since(first!.ServerTime)}");

        Assert.False(delta!.IsFull);
        var synced = Assert.Single(delta.Animals, a => a.Id == animal.Id);
        Assert.Equal((401m, new DateOnly(2026, 10, 2), 3.5m), (synced.LatestWeightKg, synced.LatestWeighedOn, synced.LatestBcs));
    }

    [Fact]
    public async Task Nothing_has_changed_after_a_future_cursor()
    {
        var (_, officer, _) = await ClientsAsync();

        var pull = await officer.GetFromJsonAsync<SyncPullDto>($"/api/sync/pull?since={Since(DateTimeOffset.UtcNow.AddDays(1))}");

        Assert.Empty(pull!.Animals);
        Assert.Empty(pull.RemovedAnimalIds);
    }

    [Fact]
    public async Task Deleted_animals_are_removed_and_status_changes_are_sent()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var (manager, officer, farmId) = await ClientsAsync();
        var deleted = await NewAnimalAsync(officer, farmId);
        var sold = await NewAnimalAsync(officer, farmId);
        var before = await officer.GetFromJsonAsync<SyncPullDto>("/api/sync/pull");

        (await hq.DeleteAsync($"/api/animals/{deleted.Id}")).EnsureSuccessStatusCode();
        (await manager.PostAsJsonAsync($"/api/animals/{sold.Id}/status",
            new ChangeStatusRequest { Status = AnimalStatus.Sold, Date = new DateOnly(2026, 10, 1), Version = sold.Version })).EnsureSuccessStatusCode();

        var delta = await officer.GetFromJsonAsync<SyncPullDto>($"/api/sync/pull?since={Since(before!.ServerTime)}");

        Assert.Contains(deleted.Id, delta!.RemovedAnimalIds);
        Assert.DoesNotContain(delta.Animals, a => a.Id == deleted.Id);
        Assert.Equal(AnimalStatus.Sold, delta.Animals.Single(a => a.Id == sold.Id).Status);
    }

    [Fact]
    public async Task Retried_photo_upload_with_the_same_id_is_stored_once()
    {
        var (_, officer, farmId) = await ClientsAsync();
        var animal = await NewAnimalAsync(officer, farmId);
        var photoId = Guid.CreateVersion7();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        MultipartFormDataContent Upload()
        {
            var file = new ByteArrayContent(png);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return new MultipartFormDataContent { { file, "file", "p.png" }, { new StringContent(photoId.ToString()), "id" } };
        }

        var first = await officer.PostAsync($"/api/animals/{animal.Id}/photos", Upload());
        var retry = await officer.PostAsync($"/api/animals/{animal.Id}/photos", Upload());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(photoId, (await first.ReadAsync<PhotoDto>()).Id);
        Assert.Equal(photoId, (await retry.ReadAsync<PhotoDto>()).Id);
    }
}
