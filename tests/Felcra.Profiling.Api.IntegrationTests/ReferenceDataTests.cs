using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.Domain.MasterData;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReferenceDataTests(ApiFactory factory)
{
    [Fact]
    public async Task Development_seed_contains_pagoh_reference_data()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);

        var projects = await officer.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
        var breeds = await officer.GetFromJsonAsync<List<BreedDto>>("/api/breeds");
        var treatments = await officer.GetFromJsonAsync<List<TreatmentProductDto>>("/api/treatment-products");

        Assert.Contains(projects!, p => p.Code == "EPP5");
        Assert.Contains(projects!, p => p.Code == "RP4");
        Assert.Contains(breeds!, b => b.Name == "Brahman Cross");
        Assert.Contains(treatments!, t => t.Category == TreatmentCategory.Vaccine);
    }

    [Fact]
    public async Task Hq_can_maintain_breeds_and_codes_are_reusable_after_delete()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var code = TestHelpers.UniqueCode("b");

        var breed = await (await hq.PostAsJsonAsync("/api/breeds", new BreedRequest { Code = code, Name = "Kedah-Kelantan" })).ReadAsync<BreedDto>();

        var update = BreedRequest.From(breed);
        update.IsActive = false;
        var updated = await (await hq.PutAsJsonAsync($"/api/breeds/{breed.Id}", update)).ReadAsync<BreedDto>();
        Assert.False(updated.IsActive);

        Assert.Equal(HttpStatusCode.Conflict,
            (await hq.PostAsJsonAsync("/api/breeds", new BreedRequest { Code = code, Name = "Duplicate" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await hq.DeleteAsync($"/api/breeds/{breed.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await hq.PostAsJsonAsync("/api/breeds", new BreedRequest { Code = code, Name = "Recreated" })).StatusCode);
    }

    [Fact]
    public async Task Feed_ingredient_validation_rejects_impossible_values()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);

        var response = await hq.PostAsJsonAsync("/api/feed-ingredients",
            new FeedIngredientRequest { Name = "Bad grass", DryMatterPercent = 140, PricePerKg = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Non_hq_users_cannot_change_master_data()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);

        var response = await manager.PostAsJsonAsync("/api/treatment-products",
            new TreatmentProductRequest { Name = "Ivermectin", Category = TreatmentCategory.Antiparasitic });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
