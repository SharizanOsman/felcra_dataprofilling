using System.Net;
using System.Net.Http.Headers;

namespace Felcra.Profiling.UI.Shared.Auth;

/// <summary>Adds the bearer token to API calls and retries once after a refresh on 401.</summary>
public sealed class BearerTokenHandler(AuthClient auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await auth.GetAccessTokenAsync();
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || token is null) return response;

        var refreshed = await auth.GetAccessTokenAsync(forceRefresh: true);
        if (refreshed is null || refreshed == token) return response;

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
        return await base.SendAsync(request, cancellationToken);
    }
}
