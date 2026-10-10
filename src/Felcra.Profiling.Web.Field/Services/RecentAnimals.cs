using System.Text.Json;
using Microsoft.JSInterop;

namespace Felcra.Profiling.Web.Field.Services;

public sealed record RecentAnimal(Guid Id, string AnimalNo, string? TagNumber, string Category);

/// <summary>The last animals opened on this device, for quick access from the home screen.</summary>
public sealed class RecentAnimals(IJSRuntime js)
{
    private const string Key = "felcra.field.recent";
    private const int Max = 8;

    public async Task<List<RecentAnimal>> GetAsync()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            return json is null ? [] : JsonSerializer.Deserialize<List<RecentAnimal>>(json, JsonSerializerOptions.Web) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task AddAsync(RecentAnimal animal)
    {
        var list = await GetAsync();
        list.RemoveAll(a => a.Id == animal.Id);
        list.Insert(0, animal);
        await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(list.Take(Max), JsonSerializerOptions.Web));
    }
}
