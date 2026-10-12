using Felcra.Profiling.Domain.Farms;
using Microsoft.AspNetCore.Identity;

namespace Felcra.Profiling.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public required string FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Language the user last used the apps in ("ms" or "en"), for scheduled e-mails and notifications.</summary>
    public string? Language { get; set; }

    public List<UserFarm> Farms { get; set; } = [];
}

/// <summary>Assignment of a user to a farm; drives farm-scoped data access.</summary>
public class UserFarm
{
    public Guid UserId { get; set; }
    public Guid FarmId { get; set; }
    public Farm? Farm { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash of the token; the raw value is only ever held by the client.</summary>
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
