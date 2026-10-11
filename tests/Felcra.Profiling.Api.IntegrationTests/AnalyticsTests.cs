using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Analytics;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Contracts.Records;
using Felcra.Profiling.Contracts.Sync;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using Felcra.Profiling.Domain.Records;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AnalyticsTests(ApiFactory factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(8));

    private async Task<(HttpClient Hq, Guid FarmId)> NewFarmAsync()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("an"), Name = "Analytics farm", Capacity = 100 }))
            .ReadAsync<FarmDto>();
        return (hq, farm.Id);
    }

    private static async Task<AnimalDto> AnimalAsync(HttpClient hq, Guid farmId, AnimalCategory category, int ageDays) =>
        await (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farmId, TagNumber = TestHelpers.UniqueCode("g"), Category = category, Sex = Animal.SexOf(category),
            BirthDate = Today.AddDays(-ageDays), BirthDateMethod = AgeEstimationMethod.Recorded,
        })).ReadAsync<AnimalDto>();

    private static async Task WeighAsync(HttpClient hq, Guid animalId, decimal kg) =>
        (await hq.PostAsJsonAsync($"/api/animals/{animalId}/measurements", new MeasurementRequest { Type = MeasurementType.Weight, Value = kg })).EnsureSuccessStatusCode();

    [Fact]
    public async Task Growth_curve_is_fitted_per_sex_and_flags_animals_well_below_it()
    {
        var (hq, farmId) = await NewFarmAsync();
        // Females weighed across ages roughly following a Gompertz curve (mature ≈ 420 kg).
        foreach (var (ageDays, kg) in new[] { (120, 95m), (240, 150m), (365, 200m), (540, 255m), (730, 300m), (1095, 350m), (1460, 385m), (2190, 410m) })
            await WeighAsync(hq, (await AnimalAsync(hq, farmId, ageDays < 700 ? AnimalCategory.FemaleCalf : AnimalCategory.Dam, ageDays)).Id, kg);
        var thin = await AnimalAsync(hq, farmId, AnimalCategory.Dam, 1460);
        await WeighAsync(hq, thin.Id, 260);

        var growth = await hq.GetFromJsonAsync<GrowthAnalysisDto>($"/api/analytics/growth?farmId={farmId}");

        var females = growth!.Curves.Single(c => c.Sex == Sex.Female);
        Assert.NotNull(females.Fit);
        Assert.InRange(females.Fit!.MatureWeightKg, 350, 550);
        Assert.Equal(97, females.Curve.Count);
        Assert.Contains(growth.BelowCurve, b => b.AnimalId == thin.Id && b.DeviationPercent < -15);
        Assert.Null(growth.Curves.Single(c => c.Sex == Sex.Male).Fit);
    }

    [Fact]
    public async Task Lactation_curve_uses_days_since_calving()
    {
        var (hq, farmId) = await NewFarmAsync();
        var dam = await AnimalAsync(hq, farmId, AnimalCategory.Dam, 2000);
        (await hq.PostAsJsonAsync($"/api/animals/{dam.Id}/breeding-events", new BreedingEventRequest
        {
            Type = BreedingEventType.Calving, EventDate = Today.AddDays(-200), Calves = [new CalfRequest { Sex = Sex.Female }],
        })).EnsureSuccessStatusCode();
        foreach (var (dim, litres) in new[] { (10, 6.0m), (30, 8.5m), (50, 9.0m), (80, 8.6m), (110, 7.9m), (140, 7.1m), (170, 6.2m), (195, 5.6m) })
        {
            (await hq.PostAsJsonAsync($"/api/animals/{dam.Id}/measurements", new MeasurementRequest
            {
                Type = MeasurementType.MilkYield, Value = litres, MeasuredOn = Today.AddDays(-200 + dim),
            })).EnsureSuccessStatusCode();
        }

        var lactation = await hq.GetFromJsonAsync<LactationAnalysisDto>($"/api/analytics/lactation?farmId={farmId}");

        Assert.NotNull(lactation!.HerdFit);
        Assert.InRange(lactation.HerdFit!.PeakDay, 25, 80);
        Assert.InRange(lactation.HerdFit.Total305Litres, 1200, 2600);
        Assert.Equal(8, Assert.Single(lactation.Animals).TestDays);
    }

    [Fact]
    public async Task Lactation_without_milk_records_explains_what_to_do()
    {
        var (hq, farmId) = await NewFarmAsync();

        var lactation = await hq.GetFromJsonAsync<LactationAnalysisDto>($"/api/analytics/lactation?farmId={farmId}");

        Assert.Null(lactation!.HerdFit);
        Assert.Contains("No milk yields", lactation.Message);
    }

    [Fact]
    public async Task Dashboard_summarises_the_herd()
    {
        var (hq, farmId) = await NewFarmAsync();
        var dam = await AnimalAsync(hq, farmId, AnimalCategory.Dam, 2000);
        await AnimalAsync(hq, farmId, AnimalCategory.Bull, 1500);
        (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest { FarmId = farmId, Category = AnimalCategory.MaleCalf, Sex = Sex.Male })).EnsureSuccessStatusCode();
        await WeighAsync(hq, dam.Id, 380);

        var dashboard = await hq.GetFromJsonAsync<DashboardDto>($"/api/analytics/dashboard?farmId={farmId}");

        Assert.Equal((3, 100, 1, 1), (dashboard!.ActiveAnimals, dashboard.Capacity, dashboard.Untagged, dashboard.WeighedLast90Days));
        Assert.Equal(380m, dashboard.Categories.Single(c => c.Category == AnimalCategory.Dam).AverageWeightKg);
        Assert.Equal(12, dashboard.Activity.Count);
        Assert.Equal(1, dashboard.Activity[^1].Weighings);
    }

    [Fact]
    public async Task Sync_pull_carries_feed_ingredients_for_offline_rations()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var name = TestHelpers.UniqueCode("Feed ");
        (await hq.PostAsJsonAsync("/api/feed-ingredients", new FeedIngredientRequest { Name = name, DryMatterPercent = 90, PricePerKg = 1 }))
            .EnsureSuccessStatusCode();

        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var pull = await officer.GetFromJsonAsync<SyncPullDto>("/api/sync/pull");

        Assert.Contains(pull!.FeedIngredients, f => f.Name == name);
    }
}
