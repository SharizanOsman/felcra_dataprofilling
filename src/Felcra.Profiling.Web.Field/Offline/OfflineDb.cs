using System.Text.Json;
using Microsoft.JSInterop;

namespace Felcra.Profiling.Web.Field.Offline;

/// <summary>Typed access to the browser's IndexedDB stores (see wwwroot/js/offline-db.js). Values are stored as JSON.</summary>
public sealed class OfflineDb(IJSRuntime js)
{
    public const string Animals = "animals";
    public const string Records = "records";
    public const string Outbox = "outbox";
    public const string Photos = "photos";
    public const string Meta = "meta";

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public async Task<T?> GetAsync<T>(string store, string key)
    {
        var json = await js.InvokeAsync<string?>("felcraDb.get", store, key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, Json);
    }

    public async Task<List<T>> GetAllAsync<T>(string store)
    {
        var values = await js.InvokeAsync<string[]>("felcraDb.getAll", store);
        return values.Select(v => JsonSerializer.Deserialize<T>(v, Json)!).ToList();
    }

    public Task<int> CountAsync(string store) => js.InvokeAsync<int>("felcraDb.count", store).AsTask();

    public Task PutAsync<T>(string store, string key, T value) =>
        js.InvokeVoidAsync("felcraDb.put", store, key, JsonSerializer.Serialize(value, Json)).AsTask();

    public Task DeleteAsync(string store, string key) => js.InvokeVoidAsync("felcraDb.delete", store, key).AsTask();

    public Task BulkAsync<T>(string store, IEnumerable<(string Key, T Value)> puts, IEnumerable<string> deletes) =>
        js.InvokeVoidAsync("felcraDb.bulk", store,
            puts.Select(p => new object[] { p.Key, JsonSerializer.Serialize(p.Value, Json) }).ToArray(),
            deletes.ToArray()).AsTask();

    public Task ClearAsync(string store) => js.InvokeVoidAsync("felcraDb.clear", store).AsTask();

    public Task ClearAllAsync() => js.InvokeVoidAsync("felcraDb.clearAll").AsTask();
}
