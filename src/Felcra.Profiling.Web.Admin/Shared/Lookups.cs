using Felcra.Profiling.Contracts.Farms;
using Felcra.Profiling.Contracts.MasterData;
using Felcra.Profiling.UI.Shared.Api;

namespace Felcra.Profiling.Web.Admin.Shared;

/// <summary>Reference lists used by animal and census screens, loaded in one go.</summary>
public sealed class Lookups
{
    public List<FarmDto> Farms { get; private init; } = [];
    public List<ProjectDto> Projects { get; private init; } = [];
    public List<BreedDto> Breeds { get; private init; } = [];
    public List<TreatmentProductDto> Treatments { get; private init; } = [];
    public Dictionary<Guid, List<HerdGroupDto>> GroupsByFarm { get; private init; } = [];

    public static async Task<Lookups> LoadAsync(ApiClient api, bool includeGroups = false)
    {
        var farms = api.GetAsync<List<FarmDto>>("api/farms");
        var projects = api.GetAsync<List<ProjectDto>>("api/projects");
        var breeds = api.GetAsync<List<BreedDto>>("api/breeds");
        var treatments = api.GetAsync<List<TreatmentProductDto>>("api/treatment-products");
        await Task.WhenAll(farms, projects, breeds, treatments);

        var groups = new Dictionary<Guid, List<HerdGroupDto>>();
        if (includeGroups)
        {
            foreach (var farm in farms.Result)
                groups[farm.Id] = await api.GetAsync<List<HerdGroupDto>>($"api/farms/{farm.Id}/groups");
        }

        return new Lookups
        {
            Farms = farms.Result,
            Projects = projects.Result,
            Breeds = breeds.Result,
            Treatments = treatments.Result,
            GroupsByFarm = groups,
        };
    }

    public string FarmCode(Guid id) => Farms.FirstOrDefault(f => f.Id == id)?.Code ?? "?";
    public string? ProjectCode(Guid? id) => Projects.FirstOrDefault(p => p.Id == id)?.Code;
    public string? BreedName(Guid? id) => Breeds.FirstOrDefault(b => b.Id == id)?.Name;
    public string? GroupName(Guid farmId, Guid? id) => GroupsByFarm.GetValueOrDefault(farmId)?.FirstOrDefault(g => g.Id == id)?.Name;
    public List<HerdGroupDto> Groups(Guid farmId) => GroupsByFarm.GetValueOrDefault(farmId) ?? [];
}
