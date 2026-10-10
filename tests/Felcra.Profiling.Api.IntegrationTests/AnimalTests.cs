using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Common;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AnimalTests(ApiFactory factory)
{
    private async Task<Guid> PagohIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single(f => f.Code == "PAGOH").Id;

    private static AnimalRequest NewDam(Guid farmId, string? tag) => new()
    {
        FarmId = farmId, TagNumber = tag, Category = AnimalCategory.Dam, Sex = Sex.Female,
        BirthDate = new DateOnly(2022, 1, 1), BirthDateMethod = AgeEstimationMethod.Dentition,
    };

    [Fact]
    public async Task Field_officer_registers_an_animal_and_gets_a_system_number()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var farmId = await PagohIdAsync(officer);
        var tag = TestHelpers.UniqueCode("t");

        var animal = await (await officer.PostAsJsonAsync("/api/animals", NewDam(farmId, tag))).ReadAsync<AnimalDto>();

        Assert.Matches(@"^A\d{6}$", animal.AnimalNo);
        Assert.Equal(tag, animal.TagNumber);
        Assert.Equal(tag, Assert.Single(animal.TagHistory).TagNumber);
    }

    [Fact]
    public async Task Sex_must_match_category()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var request = NewDam(await PagohIdAsync(hq), null);
        request.Sex = Sex.Male;

        Assert.Equal(HttpStatusCode.BadRequest, (await hq.PostAsJsonAsync("/api/animals", request)).StatusCode);
    }

    [Fact]
    public async Task Duplicate_tags_are_allowed_but_flagged()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var farmId = await PagohIdAsync(manager);
        var tag = TestHelpers.UniqueCode("d");

        var first = await (await manager.PostAsJsonAsync("/api/animals", NewDam(farmId, tag))).ReadAsync<AnimalDto>();
        var second = await (await manager.PostAsJsonAsync("/api/animals", NewDam(farmId, tag))).ReadAsync<AnimalDto>();

        Assert.Equal([first.Id], second.DuplicateTagAnimalIds);
        var list = await manager.GetFromJsonAsync<PagedResult<AnimalListItemDto>>($"/api/animals?search={tag}");
        Assert.Equal(2, list!.TotalCount);
        Assert.All(list.Items, a => Assert.True(a.HasDuplicateTag));

        var duplicates = await manager.GetFromJsonAsync<List<DuplicateTagDto>>($"/api/animals/duplicate-tags?farmId={farmId}");
        Assert.Contains(duplicates!, d => d.TagNumber == tag && d.AnimalIds.Count == 2);
    }

    [Fact]
    public async Task Retag_keeps_tag_history_and_resolves_duplicates()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var farmId = await PagohIdAsync(manager);
        var tag = TestHelpers.UniqueCode("r");
        var animal = await (await manager.PostAsJsonAsync("/api/animals", NewDam(farmId, tag))).ReadAsync<AnimalDto>();

        var newTag = TestHelpers.UniqueCode("n");
        var retagged = await (await manager.PostAsJsonAsync($"/api/animals/{animal.Id}/retag",
            new RetagRequest { TagNumber = newTag, Date = new DateOnly(2026, 8, 1), Reason = "Lost tag", Version = animal.Version }))
            .ReadAsync<AnimalDto>();

        Assert.Equal(newTag, retagged.TagNumber);
        Assert.Equal(2, retagged.TagHistory.Count);
        Assert.Equal(new DateOnly(2026, 8, 1), retagged.TagHistory.Single(t => t.TagNumber == tag).RemovedOn);
    }

    [Fact]
    public async Task Manager_records_a_sale_and_officer_cannot_edit()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var animal = await (await manager.PostAsJsonAsync("/api/animals", NewDam(await PagohIdAsync(manager), null))).ReadAsync<AnimalDto>();

        Assert.Equal(HttpStatusCode.Forbidden, (await officer.PutAsJsonAsync($"/api/animals/{animal.Id}", AnimalRequest.From(animal))).StatusCode);

        var sold = await (await manager.PostAsJsonAsync($"/api/animals/{animal.Id}/status",
            new ChangeStatusRequest { Status = AnimalStatus.Sold, Date = new DateOnly(2026, 9, 1), Reason = "Old dam", Version = animal.Version }))
            .ReadAsync<AnimalDto>();
        Assert.Equal(AnimalStatus.Sold, sold.Status);

        var active = await manager.GetFromJsonAsync<PagedResult<AnimalListItemDto>>("/api/animals?pageSize=500");
        Assert.DoesNotContain(active!.Items, a => a.Id == animal.Id);
        var all = await manager.GetFromJsonAsync<PagedResult<AnimalListItemDto>>($"/api/animals?status=Sold&pageSize=500");
        Assert.Contains(all!.Items, a => a.Id == animal.Id);
    }

    [Fact]
    public async Task Status_all_lists_every_status_and_bad_query_values_are_400()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var animal = await (await manager.PostAsJsonAsync("/api/animals", NewDam(await PagohIdAsync(manager), null))).ReadAsync<AnimalDto>();
        (await manager.PostAsJsonAsync($"/api/animals/{animal.Id}/status",
            new ChangeStatusRequest { Status = AnimalStatus.Culled, Date = new DateOnly(2026, 9, 1), Reason = "Old", Version = animal.Version }))
            .EnsureSuccessStatusCode();

        var all = await manager.GetFromJsonAsync<PagedResult<AnimalListItemDto>>($"/api/animals?status=all&ids={animal.Id}");
        Assert.Single(all!.Items);

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/animals?farmId=not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync("/api/animals?page=abc")).StatusCode);
    }

    [Fact]
    public async Task Death_requires_a_reason()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var animal = await (await manager.PostAsJsonAsync("/api/animals", NewDam(await PagohIdAsync(manager), null))).ReadAsync<AnimalDto>();

        var response = await manager.PostAsJsonAsync($"/api/animals/{animal.Id}/status",
            new ChangeStatusRequest { Status = AnimalStatus.Dead, Date = new DateOnly(2026, 9, 1), Version = animal.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Animals_on_other_farms_are_invisible_to_managers()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var other = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("x"), Name = "Other" }))
            .ReadAsync<FarmDto>();
        var animal = await (await hq.PostAsJsonAsync("/api/animals", NewDam(other.Id, null))).ReadAsync<AnimalDto>();

        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/animals/{animal.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync("/api/animals", NewDam(other.Id, null))).StatusCode);
    }

    [Fact]
    public async Task Dam_must_be_female()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farmId = await PagohIdAsync(hq);
        var bull = await (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest
        {
            FarmId = farmId, Category = AnimalCategory.Bull, Sex = Sex.Male,
        })).ReadAsync<AnimalDto>();

        var calf = new AnimalRequest { FarmId = farmId, Category = AnimalCategory.FemaleCalf, Sex = Sex.Female, DamId = bull.Id };

        Assert.Equal(HttpStatusCode.BadRequest, (await hq.PostAsJsonAsync("/api/animals", calf)).StatusCode);
    }
}
