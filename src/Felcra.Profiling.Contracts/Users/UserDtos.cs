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
    public static IRuleBuilderOptions<TRequest, string> ValidPassword<TRequest>(this IRuleBuilder<TRequest, string> rule) => rule
        .NotEmpty()
        .MinimumLength(8)
        .Matches("[A-Z]").WithMessage(_ => T("Password must contain an uppercase letter."))
        .Matches("[a-z]").WithMessage(_ => T("Password must contain a lowercase letter."))
        .Matches("[0-9]").WithMessage(_ => T("Password must contain a digit."));

    public static IRuleBuilderOptions<TRequest, string> ValidRole<TRequest>(this IRuleBuilder<TRequest, string> rule) => rule
        .Must(r => Roles.All.Contains(r)).WithMessage(_ => T("Unknown role."));

    /// <summary>Farm managers and field officers must be assigned to at least one farm.</summary>
    public static void FarmsRequiredForFarmRoles<TRequest>(this AbstractValidator<TRequest> validator, Func<TRequest, string> role, Func<TRequest, List<Guid>> farms) =>
        validator.RuleFor(x => farms(x)).NotEmpty()
            .When(x => role(x) != Roles.HqAdmin)
            .OverridePropertyName("FarmIds")
            .WithName(_ => T("Farms"))
            .WithMessage(_ => T("Assign at least one farm to farm managers and field officers."));
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
