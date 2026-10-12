using System.Text.Json;
using Felcra.Profiling.Contracts.Sync;
using Felcra.Profiling.UI.Shared.Api;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Felcra.Profiling.Web.Field.Offline;

/// <summary>
/// Keeps the phone and the server in step: sends queued records (oldest first) and pulls farm data,
/// whenever the phone is online — on start, when the connection returns, after each save and every few minutes.
/// </summary>
public sealed class SyncService(FieldData data, ApiClient api, IJSRuntime js, AuthenticationStateProvider auth) : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DotNetObjectReference<SyncService>? _self;
    private PeriodicTimer? _timer;
    private bool _started;

    public bool IsOnline { get; private set; } = true;
    public bool IsSyncing { get; private set; }
    public int PendingCount { get; private set; }
    public int FailedCount { get; private set; }
    public int AnimalCount { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Raised whenever any of the status properties change.</summary>
    public event Action? Changed;

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;

        // Another user's farm data must not be shown to this user.
        var state = await auth.GetAuthenticationStateAsync();
        var userId = state.User.FindFirst("sub")?.Value;
        if (userId is not null && await data.UserIdAsync() != userId) await data.SwitchUserAsync(userId);

        if (_self is null)
        {
            // Browser events are subscribed once per app instance, even after sign-out and sign-in.
            _self = DotNetObjectReference.Create(this);
            await js.InvokeVoidAsync("felcraNet.subscribe", _self);
        }
        IsOnline = await js.InvokeAsync<bool>("felcraNet.isOnline");
        LastSyncedAt = await data.LastSyncAsync();
        await RefreshCountsAsync();

        _ = RunTimerAsync();
        _ = SyncNowAsync();
    }

    [JSInvokable]
    public async Task OnConnectivityChanged(bool online)
    {
        IsOnline = online;
        Changed?.Invoke();
        if (online) await SyncNowAsync();
    }

    /// <summary>Saves a record on the phone and tries to send it straight away.</summary>
    public async Task EnqueueAsync(OutboxItem item)
    {
        await data.EnqueueAsync(item);
        await RefreshCountsAsync();
        _ = SyncNowAsync();
    }

    public async Task RetryAsync(OutboxItem item)
    {
        await data.UpdateAsync(item with { Error = null });
        await RefreshCountsAsync();
        await SyncNowAsync();
    }

    public async Task DiscardAsync(OutboxItem item)
    {
        await data.RemoveAsync(item);
        await RefreshCountsAsync();
    }

    /// <summary>
    /// Makes sure the phone has the latest farm data: waits for a sync that is already running
    /// (e.g. the first download after sign-in), otherwise runs one.
    /// </summary>
    public async Task EnsureSyncedAsync()
    {
        if (!IsOnline) return;
        if (IsSyncing)
        {
            await _gate.WaitAsync();
            _gate.Release();
            return;
        }
        await SyncNowAsync();
    }

    public async Task SyncNowAsync()
    {
        if (!IsOnline || !await _gate.WaitAsync(0)) return;
        IsSyncing = true;
        Changed?.Invoke();
        try
        {
            if (await PushAsync()) await PullAsync();
        }
        catch (HttpRequestException)
        {
            // Lost the connection mid-sync; everything not yet confirmed stays queued.
        }
        catch (JsonException)
        {
            // Unexpected response (e.g. a captive portal page); try again later.
        }
        finally
        {
            IsSyncing = false;
            await RefreshCountsAsync();
            _gate.Release();
        }
    }

    /// <summary>Sends queued items in order. Returns false when it had to stop because the server is unreachable.</summary>
    private async Task<bool> PushAsync()
    {
        foreach (var item in await data.OutboxAsync())
        {
            if (item.Error is not null) continue;

            var result = await SendAsync(item);
            if (result.Succeeded)
            {
                await data.RemoveAsync(item);
            }
            else if (result.IsPermanentFailure)
            {
                await data.UpdateAsync(item with { Error = result.FullError, Attempts = item.Attempts + 1 });
            }
            else
            {
                // Offline, server error or expired session: keep the order and retry later.
                await data.UpdateAsync(item with { Attempts = item.Attempts + 1 });
                return false;
            }
            await RefreshCountsAsync();
        }
        return true;
    }

    private async Task<ApiResult<object>> SendAsync(OutboxItem item)
    {
        if (item.Kind != OutboxKind.Photo)
        {
            var body = JsonSerializer.Deserialize<JsonElement>(item.Payload);
            return await api.PostAsync<object>(item.Path, body);
        }

        var photo = await data.PhotoAsync(item.Id);
        if (photo is null)
            return new ApiResult<object>(null, T("The photo is no longer on this phone.")
, new Dictionary<string, string[]>(), System.Net.HttpStatusCode.BadRequest);
        return await api.PostFileAsync<object>(item.Path, new MemoryStream(Convert.FromBase64String(photo.Base64)), "photo.jpg", photo.ContentType,
            new Dictionary<string, string> { ["id"] = photo.Id.ToString() });
    }

    private async Task PullAsync()
    {
        var since = await data.CursorAsync();
        var url = since is null ? "api/sync/pull" : $"api/sync/pull?since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        var pull = await api.GetAsync<SyncPullDto>(url);
        await data.ApplyPullAsync(pull);
        LastSyncedAt = DateTimeOffset.UtcNow;
        await data.SetLastSyncAsync(LastSyncedAt.Value);
    }

    private async Task RefreshCountsAsync()
    {
        var outbox = await data.OutboxAsync();
        FailedCount = outbox.Count(i => i.Error is not null);
        PendingCount = outbox.Count - FailedCount;
        AnimalCount = (await data.AnimalsAsync()).Count;
        Changed?.Invoke();
    }

    private async Task RunTimerAsync()
    {
        _timer = new PeriodicTimer(Interval);
        while (await _timer.WaitForNextTickAsync()) await SyncNowAsync();
    }

    /// <summary>On sign-out: stop syncing and remove everything stored on the phone.</summary>
    public async Task ResetAsync()
    {
        _timer?.Dispose();
        _started = false;
        await data.ClearAllAsync();
        PendingCount = FailedCount = AnimalCount = 0;
        LastSyncedAt = null;
        Changed?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        _self?.Dispose();
        return ValueTask.CompletedTask;
    }
}
