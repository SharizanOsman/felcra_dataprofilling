using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Felcra.Profiling.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace Felcra.Profiling.UI.Shared.Auth;

/// <summary>
/// Builds the client-side user from the stored access token. The token is not validated here
/// (the API does that); the user counts as signed in while the refresh token is still valid,
/// so the app keeps working offline after the short-lived access token expires.
/// </summary>
public sealed class JwtAuthStateProvider(TokenStore store, TimeProvider clock) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var tokens = await store.GetAsync();
        if (tokens is null) return Anonymous;

        if (tokens.RefreshTokenExpiresAt <= clock.GetUtcNow())
        {
            await store.ClearAsync();
            return Anonymous;
        }

        return new AuthenticationState(CreatePrincipal(tokens.AccessToken));
    }

    public void NotifyChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public static ClaimsPrincipal CreatePrincipal(string accessToken)
    {
        var claims = ParsePayload(accessToken);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "jwt", nameType: "email", roleType: "role"));
    }

    private static List<Claim> ParsePayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return [];

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        var claims = new List<Claim>();
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
                claims.AddRange(property.Value.EnumerateArray().Select(v => new Claim(property.Name, v.ToString())));
            else
                claims.Add(new Claim(property.Name, property.Value.ToString()));
        }
        return claims;
    }
}
