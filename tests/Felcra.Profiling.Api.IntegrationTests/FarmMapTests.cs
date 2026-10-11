using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Animals;
using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FarmMapTests(ApiFactory factory)
{
    [Fact]
    public async Task Farm_location_is_saved_and_shown_on_the_map_with_its_herd()
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var farm = await (await hq.PostAsJsonAsync("/api/farms", new FarmRequest
        {
            Code = TestHelpers.UniqueCode("k"), Name = "Kluang farm", State = "Johor", Latitude = 2.0301m, Longitude = 103.3185m, Capacity = 80,
        })).ReadAsync<FarmDto>();
        for (var i = 0; i < 2; i++)
            (await hq.PostAsJsonAsync("/api/animals", new AnimalRequest
            {
                FarmId = farm.Id, TagNumber = TestHelpers.UniqueCode("k"), Category = AnimalCategory.Dam, Sex = Sex.Female,
            })).EnsureSuccessStatusCode();

        Assert.Equal((2.0301m, 103.3185m), (farm.Latitude, farm.Longitude));
        var point = Assert.Single((await hq.GetFromJsonAsync<List<FarmMapPointDto>>("/api/farms/map"))!, p => p.FarmId == farm.Id);
        Assert.Equal(("Johor", 2, 80), (point.State, point.ActiveAnimals, point.Capacity));
        Assert.Equal((2.0301m, 103.3185m), (point.Latitude, point.Longitude));
    }

    [Theory]
    [InlineData(51.5, -0.12, null)]        // London
    [InlineData(2.1, null, null)]          // latitude without longitude
    [InlineData(null, null, "Singapore")]  // not a Malaysian state
    public async Task Locations_outside_malaysia_and_unknown_states_are_refused(double? lat, double? lon, string? state)
    {
        var hq = await factory.CreateAuthenticatedClientAsync(ApiFactory.AdminEmail);
        var response = await hq.PostAsJsonAsync("/api/farms", new FarmRequest
        {
            Code = TestHelpers.UniqueCode("x"), Name = "Wrong place", State = state, Latitude = (decimal?)lat, Longitude = (decimal?)lon,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Managers_see_only_their_farms_on_the_map()
    {
        var manager = await factory.CreateAuthenticatedClientAsync(ApiFactory.ManagerEmail);
        var points = await manager.GetFromJsonAsync<List<FarmMapPointDto>>("/api/farms/map");
        var pagoh = Assert.Single(points!);
        Assert.Equal("PAGOH", pagoh.Code);
        Assert.Equal((2.148m, 102.771m), (pagoh.Latitude, pagoh.Longitude));
    }
}
