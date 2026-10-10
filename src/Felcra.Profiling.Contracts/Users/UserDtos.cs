using Felcra.Profiling.Contracts.Auth;
using FluentValidation;

namespace Felcra.Profiling.Contracts.Users;

public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    IReadOnlyList<Guid> FarmIds,
    bool IsActive,
    bool IsLockedOut,
    DateTimeOffset CreatedAt);

public sealed class CreateUserRequest
{
    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.FieldOfficer;
    public List<Guid> FarmIds { get; set; } = [];

    /// <summary>Initial password, given to the user by the administrator.</summary>
    public string Password { get; set; } = "";
}

public sealed class UpdateUserRequest
{
    public string FullName { get; set; } = "";
    public string Role { get; set; } = Roles.FieldOfficer;
    public List<Guid> FarmIds { get; set; } = [];
    public bool IsActive { get; set; } = true;

    public static UpdateUserRequest From(UserDto dto) => new()
    {
        FullName = dto.FullName, Role = dto.Role, FarmIds = [.. dto.FarmIds], IsActive = dto.IsActive,
    };
}

public sealed class ResetPasswordRequest
{
    public string NewPassword { get; set; } = "";
}

internal static class UserRules
{
    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty()
        .MinimumLength(8)
        .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
        .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
        .Matches("[0-9]").WithMessage("Password must contain a digit.");

    public static IRuleBuilderOptions<T, string> ValidRole<T>(this IRuleBuilder<T, string> rule) => rule
        .Must(r => Roles.All.Contains(r)).WithMessage("Unknown role.");

    /// <summary>Farm managers and field officers must be assigned to at least one farm.</summary>
    public static void FarmsRequiredForFarmRoles<T>(this AbstractValidator<T> validator, Func<T, string> role, Func<T, List<Guid>> farms) =>
        validator.RuleFor(x => farms(x)).NotEmpty()
            .When(x => role(x) != Roles.HqAdmin)
            .OverridePropertyName("FarmIds")
            .WithName("Farms")
            .WithMessage("Assign at least one farm to farm managers and field officers.");
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).ValidRole();
        RuleFor(x => x.Password).ValidPassword();
        this.FarmsRequiredForFarmRoles(x => x.Role, x => x.FarmIds);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Role).ValidRole();
        this.FarmsRequiredForFarmRoles(x => x.Role, x => x.FarmIds);
    }
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.NewPassword).ValidPassword();
    }
}
