namespace Felcra.Profiling.Application.Abstractions;

/// <summary>The user making the current request (or none, e.g. for background jobs and seeding).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    bool IsHqAdmin { get; }

    /// <summary>Farms the user is assigned to. Ignored for HQ admins, who see every farm.</summary>
    IReadOnlyCollection<Guid> FarmIds { get; }
}
