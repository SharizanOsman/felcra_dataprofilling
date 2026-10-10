using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.MasterData;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FarmTests(ApiFactory factory)
{
    [Fact]
    public async Task Hq_can_create_update_and_delete_a_farm_with_projects()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var projects = await hq.GetFromJsonAsync<List<ProjectDto>>("/api/projects");
        var epp5 = projects!.Single(p => p.Code == "EPP5");

        var created = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest
        {
            Code = TestHelpers.UniqueCode("f"), Name = "Test farm", State = "Johor", AreaHectares = 120.5m, ProjectIds = [epp5.Id],
        })).ReadAsync<FarmDto>();
        Assert.Equal([epp5.Id], created.ProjectIds);

        var update = FarmRequest.From(created);
        update.Name = "Test farm (renamed)";
        update.ProjectIds = [];
        var updated = await (await hq.PutAsJsonAsync($"/api/farms/{created.Id}", update)).ReadAsync<FarmDto>();
        Assert.Equal("Test farm (renamed)", updated.Name);
        Assert.Empty(updated.ProjectIds);
        Assert.NotEqual(created.Version, updated.Version);

        Assert.Equal(HttpStatusCode.NoContent, (await hq.DeleteAsync($"/api/farms/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hq.GetAsync($"/api/farms/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Stale_version_is_rejected_with_conflict()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("v"), Name = "Version farm" }))
            .ReadAsync<FarmDto>();

        var first = FarmRequest.From(farm);
        first.Name = "Edited by user A";
        (await hq.PutAsJsonAsync($"/api/farms/{farm.Id}", first)).EnsureSuccessStatusCode();

        var stale = FarmRequest.From(farm);
        stale.Name = "Edited by user B from an old copy";
        var response = await hq.PutAsJsonAsync($"/api/farms/{farm.Id}", stale);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_farm_code_is_rejected_with_conflict()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);

        var response = await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = "PAGOH", Name = "Duplicate" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_farm_is_rejected_with_validation_errors()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);

        var response = await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = "bad code!", Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Manager_only_sees_assigned_farms_and_cannot_create_farms()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var other = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("o"), Name = "Other" }))
            .ReadAsync<FarmDto>();

        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var visible = await manager.GetFromJsonAsync<List<FarmDto>>("/api/farms");

        Assert.Equal(["PAGOH"], visible!.Select(f => f.Code));
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/farms/{other.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await manager.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("m"), Name = "Nope" })).StatusCode);
    }

    [Fact]
    public async Task Manager_manages_groups_on_own_farm_only()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var other = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest { Code = TestHelpers.UniqueCode("g"), Name = "Other" }))
            .ReadAsync<FarmDto>();

        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var pagoh = (await manager.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single();

        var name = TestHelpers.UniqueCode("pen");
        var group = await (await manager.PostAsJsonAsync($"/api/farms/{pagoh.Id}/groups", new HerdGroupRequest { Name = name }))
            .ReadAsync<HerdGroupDto>();
        var groups = await manager.GetFromJsonAsync<List<HerdGroupDto>>($"/api/farms/{pagoh.Id}/groups");
        Assert.Contains(groups!, g => g.Id == group.Id);

        var duplicate = await manager.PostAsJsonAsync($"/api/farms/{pagoh.Id}/groups", new HerdGroupRequest { Name = name });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var otherFarm = await manager.PostAsJsonAsync($"/api/farms/{other.Id}/groups", new HerdGroupRequest { Name = "Sneaky" });
        Assert.Equal(HttpStatusCode.NotFound, otherFarm.StatusCode);
    }

    [Fact]
    public async Task Field_officer_can_read_groups_but_not_change_them()
    {
        var officer = await factory.CreateAuthenticatedClientAsync(ApiFactory.OfficerEmail);
        var pagoh = (await officer.GetFromJsonAsync<List<FarmDto>>("/api/farms"))!.Single();

        Assert.Equal(HttpStatusCode.OK, (await officer.GetAsync($"/api/farms/{pagoh.Id}/groups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await officer.PostAsJsonAsync($"/api/farms/{pagoh.Id}/groups", new HerdGroupRequest { Name = "Nope" })).StatusCode);
    }
}
