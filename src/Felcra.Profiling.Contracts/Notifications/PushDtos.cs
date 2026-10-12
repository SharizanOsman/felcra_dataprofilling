using FluentValidation;

namespace Felcra.Profiling.Contracts.Notifications;

/// <summary>VAPID public key for <c>PushManager.subscribe</c>; null when push is not set up on the server.</summary>
public sealed record PushConfigDto(string? PublicKey);

/// <summary>A browser push subscription (keys base64url, as returned by <c>PushSubscription.getKey</c>).</summary>
public sealed class PushSubscriptionRequest
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
}

public sealed class PushSubscriptionRequestValidator : AbstractValidator<PushSubscriptionRequest>
{
    public PushSubscriptionRequestValidator()
    {
        // Push services are always HTTPS; refusing anything else stops the server being used to call other URLs.
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(1000)
            .Must(e => Uri.TryCreate(e, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .WithMessage(_ => T("Endpoint must be an https URL."));
        RuleFor(x => x.P256dh).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Auth).NotEmpty().MaximumLength(100);
    }
}

public sealed record PushTestResultDto(int Sent);
