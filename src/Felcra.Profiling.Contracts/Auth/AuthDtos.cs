using FluentValidation;

namespace Felcra.Profiling.Contracts.Auth;

public sealed class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed record RefreshRequest(string RefreshToken);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> FarmIds);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
