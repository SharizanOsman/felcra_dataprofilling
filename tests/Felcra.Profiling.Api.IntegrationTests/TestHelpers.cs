using System.Net.Http.Json;

namespace Felcra.Profiling.Api.IntegrationTests;

internal static class TestHelpers
{
    /// <summary>A short unique code so tests sharing one database don't collide.</summary>
    public static string UniqueCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
