using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Contracts.Sync;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class BreedingTests(ApiFactory factory)
{
    // Malaysia is UTC+8; the API computes due dates in Malaysian time.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    /// <summary>HQ client plus a fresh farm, so herd figures are not affected by other tests.</summary>
    private async Task<(HttpClient Hq, Guid FarmId)> NewFarmAsync()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("br"), Name = "Breeding farm" }))
            .ReadAsync<FarmDto>();
        return (hq, farm.Id);
    }

    private static async Task<AnimalDto> AddAsync(HttpClient client, Guid farmId, AnimalCategory category, DateOnly? birthDate = null) =>
        await (await client.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farmId, TagNumber = TestHelpers.UniqueCode("t"), Category = category, Sex = Animal.SexOf(category),
            BirthDate = birthDate, BirthDateMethod = birthDate is null ? null : AgeEstimationMethod.Dentition,
        })).ReadAsync<AnimalDto>();

    private static async Task<HttpResponseMessage> RecordAsync(HttpClient client, Guid animalId, BreedingEventRequest request) =>
        await client.PostAsJsonAsync($"/api/animals/{animalId}/breeding-events", request);

    private static async Task<AnimalBreedingDto> BreedingAsync(HttpClient client, Guid animalId) =>
        (await client.GetFromJsonAsync<AnimalBreedingDto>($"/api/animals/{animalId}/breeding"))!;

    [Fact]
    public async Task Service_then_positive_check_makes_a_dam_pregnant_with_a_due_date()
    {
        var (hq, farmId) = await NewFarmAsync();
        var dam = await AddAsync(hq, farmId, AnimalCategory.Dam);

        (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.Insemination, EventDate = Today.AddDays(-50), SemenCode = "BRH-001" }))
            .EnsureSuccessStatusCode();
        var served = await BreedingAsync(hq, dam.Id);
        Assert.Equal(ReproductiveState.Served, served.Reproduction!.State);
        var reminders = await hq.GetFromJsonAsync<List<ReminderDto>>($"/api/reminders?farmId={farmId}");
        Assert.Contains(reminders!, r => r.AnimalId == dam.Id && r.Type == ReminderType.PregnancyCheckDue && r.IsOverdueOn(Today));

        (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.PregnancyCheck, EventDate = Today, PregnancyResult = PregnancyResult.Pregnant }))
            .EnsureSuccessStatusCode();
        var pregnant = await BreedingAsync(hq, dam.Id);
        Assert.Equal(ReproductiveState.Pregnant, pregnant.Reproduction!.State);
        Assert.Equal(Today.AddDays(-50 + 285), pregnant.Reproduction.ExpectedCalving);
        Assert.DoesNotContain(await hq.GetFromJsonAsync<List<ReminderDto>>($"/api/reminders?farmId={farmId}") ?? [],
            r => r.AnimalId == dam.Id && r.Type == ReminderType.PregnancyCheckDue);
    }

    [Fact]
    public async Task Calving_due_soon_appears_in_reminders()
    {
        var (hq, farmId) = await NewFarmAsync();
        var dam = await AddAsync(hq, farmId, AnimalCategory.Dam);
        (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.NaturalService, EventDate = Today.AddDays(-280) })).EnsureSuccessStatusCode();
        (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.PregnancyCheck, EventDate = Today.AddDays(-200), PregnancyResult = PregnancyResult.Pregnant }))
            .EnsureSuccessStatusCode();

        var reminders = await hq.GetFromJsonAsync<List<ReminderDto>>($"/api/reminders?farmId={farmId}&days=14");

        var calving = Assert.Single(reminders!, r => r.AnimalId == dam.Id);
        Assert.Equal((ReminderType.CalvingDue, Today.AddDays(5)), (calving.Type, calving.DueDate));
    }

    [Fact]
    public async Task Calving_registers_the_calf_and_promotes_a_heifer_once()
    {
        var (hq, farmId) = await NewFarmAsync();
        var heifer = await AddAsync(hq, farmId, AnimalCategory.FemaleCalf, Today.AddYears(-2));
        var bull = await AddAsync(hq, farmId, AnimalCategory.Bull);
        (await RecordAsync(hq, heifer.Id, new() { Type = BreedingEventType.NaturalService, EventDate = Today.AddDays(-290), SireId = bull.Id }))
            .EnsureSuccessStatusCode();

        var calving = new BreedingEventRequest
        {
            Type = BreedingEventType.Calving, EventDate = Today.AddDays(-1), Assisted = true,
            Calves = [new CalfRequest { Sex = Sex.Male, TagNumber = TestHelpers.UniqueCode("c"), BirthWeightKg = 28 }],
        };
        var first = await RecordAsync(hq, heifer.Id, calving);
        var retry = await RecordAsync(hq, heifer.Id, calving);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);

        var calfId = Assert.Single((await first.ReadAsync<BreedingEventDto>()).CalfIds);
        var calf = await hq.GetFromJsonAsync<AnimalDto>($"/api/animals/{calfId}");
        Assert.Equal((AnimalCategory.MaleCalf, Today.AddDays(-1), AgeEstimationMethod.Recorded, heifer.Id, bull.Id),
            (calf!.Category, calf.BirthDate, calf.BirthDateMethod, calf.DamId, calf.SireId));
        var birthWeight = Assert.Single((await hq.GetFromJsonAsync<AnimalRecordsDto>($"/api/animals/{calfId}/records"))!.Measurements);
        Assert.Equal(28m, birthWeight.Value);

        var dam = await hq.GetFromJsonAsync<AnimalDto>($"/api/animals/{heifer.Id}");
        Assert.Equal(AnimalCategory.Dam, dam!.Category);
        var breeding = await BreedingAsync(hq, heifer.Id);
        Assert.Single(breeding.Offspring);
        Assert.Equal((ReproductiveState.Open, 1), (breeding.Reproduction!.State, breeding.Reproduction.CalvingCount));
    }

    [Theory]
    [InlineData(BreedingEventType.PregnancyCheck, "result")]
    [InlineData(BreedingEventType.Calving, "calf")]
    public async Task Incomplete_events_are_rejected(BreedingEventType type, string expectedMessage)
    {
        var (hq, farmId) = await NewFarmAsync();
        var dam = await AddAsync(hq, farmId, AnimalCategory.Dam);

        var response = await RecordAsync(hq, dam.Id, new() { Type = type });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedMessage, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Events_must_suit_the_animals_sex()
    {
        var (hq, farmId) = await NewFarmAsync();
        var dam = await AddAsync(hq, farmId, AnimalCategory.Dam);
        var bull = await AddAsync(hq, farmId, AnimalCategory.Bull);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await RecordAsync(hq, bull.Id, new() { Type = BreedingEventType.Heat })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.BullSoundnessExam, SoundnessResult = SoundnessResult.Satisfactory })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.NaturalService, SireId = dam.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await RecordAsync(hq, bull.Id, new() { Type = BreedingEventType.BullSoundnessExam, SoundnessResult = SoundnessResult.Satisfactory })).StatusCode);
    }

    [Fact]
    public async Task Old_and_repeatedly_empty_dams_are_culling_candidates()
    {
        var (hq, farmId) = await NewFarmAsync();
        var old = await AddAsync(hq, farmId, AnimalCategory.Dam, Today.AddYears(-9));
        var empty = await AddAsync(hq, farmId, AnimalCategory.Dam, Today.AddYears(-5));
        var good = await AddAsync(hq, farmId, AnimalCategory.Dam, Today.AddYears(-4));
        (await RecordAsync(hq, empty.Id, new() { Type = BreedingEventType.Calving, EventDate = Today.AddMonths(-20),
            Calves = [new CalfRequest { Sex = Sex.Female }] })).EnsureSuccessStatusCode();
        foreach (var monthsAgo in new[] { 10, 6 })
        {
            (await RecordAsync(hq, empty.Id, new() { Type = BreedingEventType.Insemination, EventDate = Today.AddMonths(-monthsAgo) })).EnsureSuccessStatusCode();
            (await RecordAsync(hq, empty.Id, new() { Type = BreedingEventType.PregnancyCheck, EventDate = Today.AddMonths(-monthsAgo).AddDays(50),
                PregnancyResult = PregnancyResult.Open })).EnsureSuccessStatusCode();
        }
        (await hq.PostAsJsonAsync($"/api/animals/{good.Id}/measurements", new MeasurementRequest
            { Type = Domain.Records.MeasurementType.BodyCondition, Value = 3 })).EnsureSuccessStatusCode();

        var candidates = await hq.GetFromJsonAsync<List<CullingCandidateDto>>($"/api/breeding/culling-candidates?farmId={farmId}");

        Assert.Contains(candidates!.Single(c => c.AnimalId == old.Id).Reasons, r => r.StartsWith("Aged 9"));
        var emptyReasons = candidates!.Single(c => c.AnimalId == empty.Id).Reasons;
        Assert.Contains(emptyReasons, r => r.StartsWith("Not pregnant 20 months"));
        Assert.Contains(emptyReasons, r => r.StartsWith("Empty at 2"));
        Assert.DoesNotContain(candidates!, c => c.AnimalId == good.Id);

        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await officer.GetAsync("/api/breeding/culling-candidates")).StatusCode);
    }

    [Fact]
    public async Task Herd_summary_gives_pregnancy_and_calving_rates()
    {
        var (hq, farmId) = await NewFarmAsync();
        var pregnant = await AddAsync(hq, farmId, AnimalCategory.Dam);
        var calved = await AddAsync(hq, farmId, AnimalCategory.Dam);
        await AddAsync(hq, farmId, AnimalCategory.Dam);
        await AddAsync(hq, farmId, AnimalCategory.Dam);
        (await RecordAsync(hq, pregnant.Id, new() { Type = BreedingEventType.PregnancyCheck, EventDate = Today, PregnancyResult = PregnancyResult.Pregnant,
            GestationDays = 90 })).EnsureSuccessStatusCode();
        (await RecordAsync(hq, calved.Id, new() { Type = BreedingEventType.Calving, EventDate = Today.AddDays(-30),
            Calves = [new CalfRequest { Sex = Sex.Male }, new CalfRequest { Sex = Sex.Female }] })).EnsureSuccessStatusCode();

        var summary = Assert.Single((await hq.GetFromJsonAsync<List<HerdBreedingSummaryDto>>($"/api/breeding/summary?farmId={farmId}"))!);

        Assert.Equal((4, 1, 1, 2), (summary.Dams, summary.Pregnant, summary.Open, summary.NoRecords));
        Assert.Equal(25m, summary.PregnancyRatePercent);
        Assert.Equal((2, 50m), (summary.CalvesLast12Months, summary.CalvingRatePercent));
    }

    [Fact]
    public async Task Calvings_with_calves_cannot_be_deleted_but_other_events_can()
    {
        var (hq, farmId) = await NewFarmAsync();
        var dam = await AddAsync(hq, farmId, AnimalCategory.Dam);
        var heat = await (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.Heat, EventDate = Today.AddDays(-10) })).ReadAsync<BreedingEventDto>();
        var calving = await (await RecordAsync(hq, dam.Id, new() { Type = BreedingEventType.Calving, EventDate = Today,
            Calves = [new CalfRequest { Sex = Sex.Female }] })).ReadAsync<BreedingEventDto>();

        Assert.Equal(HttpStatusCode.Conflict, (await hq.DeleteAsync($"/api/breeding-events/{calving.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await hq.DeleteAsync($"/api/breeding-events/{heat.Id}")).StatusCode);
    }

    [Fact]
    public async Task Sync_pull_carries_reproduction_status_and_reminders()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var farmId = (await manager.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single().Id;
        var dam = await AddAsync(manager, farmId, AnimalCategory.Dam);
        (await RecordAsync(manager, dam.Id, new() { Type = BreedingEventType.Insemination, EventDate = Today.AddDays(-46) })).EnsureSuccessStatusCode();

        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var pull = await officer.GetFromJsonAsync<SyncPullDto>("/api/sync/pull");

        Assert.Equal(ReproductiveState.Served, pull!.Animals.Single(a => a.Id == dam.Id).Reproduction);
        Assert.Contains(pull.Reminders, r => r.AnimalId == dam.Id && r.Type == ReminderType.PregnancyCheckDue);
    }
}
