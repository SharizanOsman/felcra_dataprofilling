using Felcra.Profiling.Contracts.Notifications;
using Felcra.Profiling.UI.Shared.Api;
using Microsoft.JSInterop;

namespace Felcra.Profiling.Web.Field.Services;

public enum PushState
{
    /// <summary>The browser cannot receive push (e.g. iPhone Safari before the app is added to the Home Screen).</summary>
    Unsupported,
    /// <summary>The server has no push keys.</summary>
    NotConfigured,
    /// <summary>The user blocked notifications for this site.</summary>
    Denied,
    Off,
    On,
}

/// <summary>Turns morning task notifications on or off for this device (Web Push, see wwwroot/js/push.js).</summary>
public sealed class PushClient(IJSRuntime js, ApiClient api)
{
    private sealed record Subscription(string Endpoint, string P256dh, string Auth);

    public async Task<PushState> StateAsync()
    {
        var browser = await js.InvokeAsync<string>("felcraPush.status");
        if (browser == "unsupported") return PushState.Unsupported;
        var config = await api.GetAsync<PushConfigDto>("api/push/config");
        if (config.PublicKey is null) return PushState.NotConfigured;
        return browser switch { "denied" => PushState.Denied, "on" => PushState.On, _ => PushState.Off };
    }

    /// <summary>Asks permission, subscribes and registers the device. Returns an error message, or null when done.</summary>
    public async Task<string?> EnableAsync()
    {
        var config = await api.GetAsync<PushConfigDto>("api/push/config");
        if (config.PublicKey is null) return "Notifications are not set up on the server.";
        Subscription? subscription;
        try
        {
            subscription = await js.InvokeAsync<Subscription?>("felcraPush.subscribe", config.PublicKey);
        }
        catch (JSException ex)
        {
            return $"This browser could not turn on notifications ({ex.Message}).";
        }
        if (subscription is null) return "Notifications were not allowed. You can allow them in the browser's site settings.";

        var result = await api.PostAsync<object>("api/push/subscriptions",
            new PushSubscriptionRequest { Endpoint = subscription.Endpoint, P256dh = subscription.P256dh, Auth = subscription.Auth });
        return result.Succeeded ? null : result.FullError;
    }

    /// <summary>Unsubscribes this device here and on the server (also used when signing out of a shared phone).</summary>
    public async Task DisableAsync()
    {
        var endpoint = await js.InvokeAsync<string?>("felcraPush.unsubscribe");
        if (endpoint is not null) await api.DeleteAsync($"api/push/subscriptions?endpoint={Uri.EscapeDataString(endpoint)}");
    }

    public Task<ApiResult<PushTestResultDto>> SendTestAsync() => api.PostAsync<PushTestResultDto>("api/push/test", new { });
}
