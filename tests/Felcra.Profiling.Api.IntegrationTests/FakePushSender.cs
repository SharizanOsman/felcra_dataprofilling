using System.Collections.Concurrent;
using Felcra.Profiling.Api.Notifications;
using Felcra.Profiling.Domain.Notifications;

namespace Felcra.Profiling.Api.IntegrationTests;

/// <summary>Records push messages instead of sending them. Endpoints containing "gone" behave as expired.</summary>
internal sealed class FakePushSender : IPushSender
{
    public ConcurrentQueue<(string Endpoint, PushPayload Payload)> Sent { get; } = new();

    public bool IsConfigured => true;

    public Task<PushOutcome> SendAsync(PushSubscription subscription, PushPayload payload, CancellationToken ct)
    {
        if (subscription.Endpoint.Contains("gone")) return Task.FromResult(PushOutcome.Gone);
        Sent.Enqueue((subscription.Endpoint, payload));
        return Task.FromResult(PushOutcome.Sent);
    }

    public IReadOnlyList<PushPayload> To(string endpoint) => Sent.Where(s => s.Endpoint == endpoint).Select(s => s.Payload).ToList();
}
