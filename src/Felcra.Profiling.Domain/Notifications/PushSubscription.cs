using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.Notifications;

/// <summary>A browser / phone that agreed to receive push notifications for a user (Web Push).</summary>
public class PushSubscription : Entity
{
    public Guid UserId { get; set; }

    /// <summary>Push service URL for this device; unique.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Device public key and auth secret (base64url) used to encrypt messages.</summary>
    public required string P256dh { get; set; }
    public required string Auth { get; set; }

    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSentAt { get; set; }
}

/// <summary>One day's task notifications; the date is unique so they go out once per day.</summary>
public class ReminderNotificationRun : Entity
{
    public DateOnly Date { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int NotificationsSent { get; set; }
}
