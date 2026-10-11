using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Analytics;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Auditing;
using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Api.IntegrationTests;

/// <summary>Gaps closed before UAT: HQ can restore deleted animals (FR-AUD-02); the dashboard shows mortality (FR-RPT-01).</summary>
[Collection(ApiCollection.Name)]
public class RestoreTests(ApiFactory factory)
{
    [Fact]
    public async Task Hq_restores_a_deleted_animal_with_its_census_entry()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("d"), Name = "Restore farm" })).ReadAsync<FarmDto>();
        var animal = await (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farm.Id, TagNumber = TestHelpers.UniqueCode("r"), Category = AnimalCategory.Dam, Sex = Sex.Female,
        })).ReadAsync<AnimalDto>();
        var session = await (await hq.PostAsJsonAsync("/api/census-sessions", new CensusSessionRequest
        {
            FarmId = farm.Id, Title = "Count", StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 1),
        })).ReadAsync<CensusSessionDto>();
        (await hq.PostAsJsonAsync($"/api/census-sessions/{session.Id}/entries", new CensusEntryRequest
        {
            AnimalId = animal.Id, Category = AnimalCategory.Dam, WeightKg = 300,
        })).EnsureSuccessStatusCode();

        (await hq.DeleteAsync($"/api/animals/{animal.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await hq.GetAsync($"/api/animals/{animal.Id}")).StatusCode);
        Assert.Empty((await hq.GetFromJsonAsync<List<CensusEntryDto>>($"/api/census-sessions/{session.Id}/entries"))!);
        var deleted = Assert.Single((await hq.GetFromJsonAsync<List<DeletedAnimalDto>>("/api/animals/deleted"))!, d => d.Id == animal.Id);
        Assert.Equal("HQ Administrator", deleted.DeletedBy);

        Assert.Equal(HttpStatusCode.NoContent, (await hq.PostAsync($"/api/animals/{animal.Id}/restore", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await hq.GetAsync($"/api/animals/{animal.Id}")).StatusCode);
        Assert.Single((await hq.GetFromJsonAsync<List<CensusEntryDto>>($"/api/census-sessions/{session.Id}/entries"))!);
        Assert.DoesNotContain((await hq.GetFromJsonAsync<List<DeletedAnimalDto>>("/api/animals/deleted"))!, d => d.Id == animal.Id);
        var audit = await hq.GetFromJsonAsync<PagedResult<AuditLogDto>>($"/api/audit-logs?entityType=Animal&entityId={animal.Id}");
        Assert.Contains(audit!.Items, a => a.Action == "Restored");
        // Restoring something that is not deleted is "not found".
        Assert.Equal(HttpStatusCode.NotFound, (await hq.PostAsync($"/api/animals/{animal.Id}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Only_hq_sees_and_restores_deleted_animals()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/animals/deleted")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/animals/{Guid.NewGuid()}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Dashboard_reports_deaths_and_mortality()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("m"), Name = "Mortality farm" })).ReadAsync<FarmDto>();
        var animals = new List<AnimalDto>();
        for (var i = 0; i < 4; i++)
        {
            animals.Add(await (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest
            {
                FarmId = farm.Id, TagNumber = TestHelpers.UniqueCode("m"), Category = AnimalCategory.Dam, Sex = Sex.Female,
            })).ReadAsync<AnimalDto>());
        }
        (await hq.PostAsJsonAsync($"/api/animals/{animals[0].Id}/status", new ChangeStatusRequest
        {
            Status = AnimalStatus.Dead, Date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8)).AddDays(-10), Reason = "Bloat", Version = animals[0].Version,
        })).EnsureSuccessStatusCode();

        var dashboard = await hq.GetFromJsonAsync<DashboardDto>($"/api/analytics/dashboard?farmId={farm.Id}");

        Assert.Equal(1, dashboard!.DeathsLast12Months);
        Assert.Equal(25m, dashboard.MortalityPercent); // 1 death / (3 active + 1)
    }
}
