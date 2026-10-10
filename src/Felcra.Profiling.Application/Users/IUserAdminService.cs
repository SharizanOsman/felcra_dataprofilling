using Felcra.Profiling.Contracts.Users;

namespace Felcra.Profiling.Application.Users;

/// <summary>Outcome of a user-management command.</summary>
public sealed record UserCommandResult(UserDto? User, bool NotFound, IDictionary<string, string[]> Errors)
{
    public bool Succeeded => User is not null;

    public static UserCommandResult Ok(UserDto user) => new(user, false, new Dictionary<string, string[]>());
    public static UserCommandResult Missing() => new(null, true, new Dictionary<string, string[]>());
    public static UserCommandResult Invalid(string key, params string[] messages) =>
        new(null, false, new Dictionary<string, string[]> { [key] = messages });
}

/// <summary>User administration for HQ admins.</summary>
public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken ct = default);
    Task<UserDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserCommandResult> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    /// <param name="actingUserId">The admin making the change; they may not deactivate or demote themselves.</param>
    Task<UserCommandResult> UpdateAsync(Guid id, UpdateUserRequest request, Guid actingUserId, CancellationToken ct = default);

    Task<UserCommandResult> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct = default);
}
