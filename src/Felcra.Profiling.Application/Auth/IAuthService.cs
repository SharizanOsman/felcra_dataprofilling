using Felcra.Profiling.Contracts.Auth;

namespace Felcra.Profiling.Application.Auth;

public interface IAuthService
{
    /// <summary>Returns tokens, or null when the credentials are invalid or the account is locked/inactive.</summary>
    Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Rotates a refresh token. Returns null when it is unknown, expired or revoked.</summary>
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<CurrentUserDto?> GetUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Changes the user's own password and signs out their other devices. Returns new tokens for this device,
    /// or null when the current password is wrong (counted towards lockout).
    /// </summary>
    Task<TokenResponse?> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);
}
