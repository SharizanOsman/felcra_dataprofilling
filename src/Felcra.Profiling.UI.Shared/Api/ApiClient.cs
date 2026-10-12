using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Felcra.Profiling.Localization;


namespace Felcra.Profiling.UI.Shared.Api;

/// <summary>Result of a write call: either a value, or a user-readable error (plus per-field validation errors).</summary>
/// <param name="StatusCode">HTTP status; null when the server could not be reached.</param>
public sealed record ApiResult<T>(T? Value, string? Error, IReadOnlyDictionary<string, string[]> FieldErrors, HttpStatusCode? StatusCode = null)
{
    public bool Succeeded => Error is null;

    /// <summary>
    /// The request itself was rejected (invalid, forbidden, not found, conflict): sending it again will not help.
    /// Anything else (no connection, server error, expired session) is worth retrying later.
    /// </summary>
    public bool IsPermanentFailure => StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden
        or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.RequestEntityTooLarge;

    /// <summary>Error text including any field errors, for showing in a single alert.</summary>
    public string? FullError => Error is null
        ? null
        : FieldErrors.Count == 0 ? Error : string.Join(" ", FieldErrors.Values.SelectMany(v => v));
}

/// <summary>A downloaded file.</summary>
public sealed record DownloadedFile(string FileName, string ContentType, byte[] Content);

/// <summary>Thin wrapper over the authenticated <see cref="HttpClient"/> that maps API errors to messages.</summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFieldErrors = new Dictionary<string, string[]>();

    public async Task<T> GetAsync<T>(string url) => (await http.GetFromJsonAsync<T>(url))!;

    /// <summary>Like <see cref="GetAsync{T}"/>, but failures come back as a message instead of an exception.</summary>
    public Task<ApiResult<T>> TryGetAsync<T>(string url) => SendAsync<T>(() => http.GetAsync(url));


    public async Task<ApiResult<T>> PostAsync<T>(string url, object body) => await SendAsync<T>(() => http.PostAsJsonAsync(url, body));

    public async Task<ApiResult<T>> PutAsync<T>(string url, object body) => await SendAsync<T>(() => http.PutAsJsonAsync(url, body));

    /// <summary>Uploads a file as multipart/form-data field "file", with optional extra form fields.</summary>
    public async Task<ApiResult<T>> PostFileAsync<T>(
        string url, Stream content, string fileName, string? contentType, IReadOnlyDictionary<string, string>? fields = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        if (contentType is { Length: > 0 }) file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        foreach (var (name, value) in fields ?? new Dictionary<string, string>())
            form.Add(new StringContent(value), name);
        return await SendAsync<T>(() => http.PostAsync(url, form));
    }

    public Task<byte[]> GetBytesAsync(string url) => http.GetByteArrayAsync(url);

    /// <summary>Downloads a file (e.g. a report), named as the server suggests.</summary>
    public Task<ApiResult<DownloadedFile>> DownloadAsync(string url) =>
        SendAsync(() => http.GetAsync(url), async response =>
        {
            var disposition = response.Content.Headers.ContentDisposition;
            return new DownloadedFile(
                disposition?.FileNameStar ?? disposition?.FileName?.Trim('"') ?? "download",
                response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                await response.Content.ReadAsByteArrayAsync());
        });

    public async Task<ApiResult<bool>> DeleteAsync(string url)
    {
        var result = await SendAsync<object>(() => http.DeleteAsync(url));
        return new ApiResult<bool>(result.Succeeded, result.Error, result.FieldErrors, result.StatusCode);
    }

    private static async Task<ApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send, Func<HttpResponseMessage, Task<T>>? read = null)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (HttpRequestException)
        {
            return new(default, Text.T("Cannot reach the server. Check your connection and try again."), NoFieldErrors);
        }

        if (response.IsSuccessStatusCode)
        {
            var value = read is not null ? await read(response)
                : response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0 ? default
                : await response.Content.ReadFromJsonAsync<T>();
            return new(value, null, NoFieldErrors, response.StatusCode);
        }

        var problem = await ReadProblemAsync(response);
        var message = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => Text.T("Please correct the highlighted fields."),
            HttpStatusCode.Unauthorized => Text.T("Your session has expired. Please sign in again."),
            HttpStatusCode.Forbidden => Text.T("You do not have permission to do this."),
            HttpStatusCode.NotFound => Text.T("The record was not found. It may have been deleted."),
            HttpStatusCode.Conflict => problem?.Detail ?? Text.T("The change conflicts with existing data."),
            _ => problem?.Detail ?? problem?.Title ?? Text.T("Unexpected error ({0}).", (int)response.StatusCode),
        };
        return new(default, message, problem?.Errors ?? NoFieldErrors, response.StatusCode);
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
