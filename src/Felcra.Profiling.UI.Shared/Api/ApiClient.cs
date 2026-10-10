using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Felcra.Profiling.UI.Shared.Api;

/// <summary>Result of a write call: either a value, or a user-readable error (plus per-field validation errors).</summary>
public sealed record ApiResult<T>(T? Value, string? Error, IReadOnlyDictionary<string, string[]> FieldErrors)
{
    public bool Succeeded => Error is null;

    /// <summary>Error text including any field errors, for showing in a single alert.</summary>
    public string? FullError => Error is null
        ? null
        : FieldErrors.Count == 0 ? Error : string.Join(" ", FieldErrors.Values.SelectMany(v => v));
}

/// <summary>Thin wrapper over the authenticated <see cref="HttpClient"/> that maps API errors to messages.</summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFieldErrors = new Dictionary<string, string[]>();

    public async Task<T> GetAsync<T>(string url) => (await http.GetFromJsonAsync<T>(url))!;

    public async Task<ApiResult<T>> PostAsync<T>(string url, object body) => await SendAsync<T>(() => http.PostAsJsonAsync(url, body));

    public async Task<ApiResult<T>> PutAsync<T>(string url, object body) => await SendAsync<T>(() => http.PutAsJsonAsync(url, body));

    public async Task<ApiResult<bool>> DeleteAsync(string url)
    {
        var result = await SendAsync<object>(() => http.DeleteAsync(url));
        return new ApiResult<bool>(result.Succeeded, result.Error, result.FieldErrors);
    }

    private static async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException)
        {
            return new(default, "Cannot reach the server. Check your connection and try again.", NoFieldErrors);
        }

        if (response.IsSuccessStatusCode)
        {
            var value = response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0
                ? default
                : await response.Content.ReadFromJsonAsync<T>();
            return new(value, null, NoFieldErrors);
        }

        var problem = await ReadProblemAsync(response);
        var message = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Please correct the highlighted fields.",
            HttpStatusCode.Forbidden => "You do not have permission to do this.",
            HttpStatusCode.NotFound => "The record was not found. It may have been deleted.",
            HttpStatusCode.Conflict => problem?.Detail ?? "The change conflicts with existing data.",
            _ => problem?.Detail ?? problem?.Title ?? $"Unexpected error ({(int)response.StatusCode}).",
        };
        return new(default, message, problem?.Errors ?? NoFieldErrors);
    }

    private static async Task<Problem?> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>(JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Problem(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}
