using System.Text.Json;
using Felcra.Profiling.Contracts.Auth;
using Microsoft.JSInterop;

namespace Felcra.Profiling.UI.Shared.Auth;

/// <summary>
/// Persists tokens in localStorage so the field PWA stays signed in across restarts and while offline.
/// Trade-off: tokens are readable by injected script, so XSS defences (CSP, Step 9) matter.
/// </summary>
public sealed class TokenStore(IJSRuntime js, ClientAuthOptions options)
{
    private string Key => $"felcra.auth.{options.AppName}";
    private TokenResponse? _cached;
    private bool _loaded;

    public async ValueTask<TokenResponse?> GetAsync()
    {
        if (_loaded) return _cached;

        var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        _cached = json is null ? null : JsonSerializer.Deserialize<TokenResponse>(json, JsonSerializerOptions.Web);
        _loaded = true;
        return _cached;
    }

    public async ValueTask SetAsync(TokenResponse tokens)
    {
        _cached = tokens;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(tokens, JsonSerializerOptions.Web));
    }

    public async ValueTask ClearAsync()
    {
        _cached = null;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
    }
}
