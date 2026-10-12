using System.Net;
using System.Net.Http.Json;
using Felcra.Profiling.Contracts.Auth;

namespace Felcra.Profiling.UI.Shared.Auth;

public sealed class ClientAuthOptions
{
    /// <summary>Distinguishes token storage between the admin and field apps on the same origin.</summary>
    public required string AppName { get; init; }

    /// <summary>Roles permitted to sign in to this app.</summary>
    public required IReadOnlyList<string> AllowedRoles { get; init; }
}

public sealed record LoginResult(bool Succeeded, string? Error = null);

/// <summary>Calls the anonymous auth endpoints and keeps <see cref="TokenStore"/> up to date.</summary>
public sealed class AuthClient(
    IHttpClientFactory httpFactory,
    TokenStore store,
    JwtAuthStateProvider authState,
    ClientAuthOptions options,
    TimeProvider clock)
{
    public const string AnonymousClient = "felcra-anonymous";
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private HttpClient Http => httpFactory.CreateClient(AnonymousClient);

    public async Task<LoginResult> LoginAsync(LoginRequest request)
    {
        HttpResponseMessage response;
        try
        {
            response = await Http.PostAsJsonAsync("api/auth/login", request);
        }
        catch (HttpRequestException)
        {
            return new(false, T("Cannot reach the server. Check your connection and try again."));
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return new(false, T("Too many attempts. Please wait a minute and try again."));
        if (!response.IsSuccessStatusCode)
            return new(false, T("Invalid email or password."));

        var tokens = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        var principal = JwtAuthStateProvider.CreatePrincipal(tokens.AccessToken);
        if (!options.AllowedRoles.Any(principal.IsInRole))
        {
            await LogoutOnServerAsync(tokens.RefreshToken);
            return new(false, T("Your account does not have access to this application."));
        }

        await store.SetAsync(tokens);
        authState.NotifyChanged();
        return new(true);
    }

    /// <summary>Stores new tokens issued by the server (e.g. after changing the password).</summary>
    public async Task SaveTokensAsync(TokenResponse tokens)
    {
        await store.SetAsync(tokens);
        authState.NotifyChanged();
    }

    public async Task LogoutAsync()
    {
        var tokens = await store.GetAsync();
        await store.ClearAsync();
        authState.NotifyChanged();
        if (tokens is not null) await LogoutOnServerAsync(tokens.RefreshToken);
    }

    /// <summary>
    /// Returns a usable access token, refreshing it when it is about to expire. Returns null when
    /// signed out; returns the stale token when offline so the caller's request fails normally.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(bool forceRefresh = false)
    {
        var tokens = await store.GetAsync();
        if (tokens is null) return null;
        if (!forceRefresh && tokens.AccessTokenExpiresAt > clock.GetUtcNow().AddSeconds(30)) return tokens.AccessToken;

        await RefreshLock.WaitAsync();
        try
        {
            var current = await store.GetAsync();
            if (current is null) return null;
            // Another caller refreshed while we waited.
            if (current.AccessToken != tokens.AccessToken) return current.AccessToken;

            HttpResponseMessage response;
            try
            {
                response = await Http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(current.RefreshToken));
            }
            catch (HttpRequestException)
            {
                return current.AccessToken;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await store.ClearAsync();
                authState.NotifyChanged();
                return null;
            }
            if (!response.IsSuccessStatusCode) return current.AccessToken;

            var refreshed = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
            await store.SetAsync(refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task LogoutOnServerAsync(string refreshToken)
    {
        try
        {
            await Http.PostAsJsonAsync("api/auth/logout", new RefreshRequest(refreshToken));
        }
        catch (HttpRequestException)
        {
            // Offline: the server-side token simply expires.
        }
    }
}
