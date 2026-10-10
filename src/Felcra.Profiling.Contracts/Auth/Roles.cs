namespace Felcra.Profiling.Contracts.Auth;

public static class Roles
{
    /// <summary>Headquarters administrator — full access to all farms and configuration.</summary>
    public const string HqAdmin = "HqAdmin";

    /// <summary>Farm manager — manages assigned farms, reviews data and reports.</summary>
    public const string FarmManager = "FarmManager";

    /// <summary>Field officer — records animal data at the pen via the field app.</summary>
    public const string FieldOfficer = "FieldOfficer";

    public static readonly IReadOnlyList<string> All = [HqAdmin, FarmManager, FieldOfficer];

    /// <summary>Roles allowed to sign in to the admin web portal.</summary>
    public static readonly IReadOnlyList<string> AdminPortal = [HqAdmin, FarmManager];

    /// <summary>Roles allowed to sign in to the field PWA.</summary>
    public static readonly IReadOnlyList<string> FieldApp = [FarmManager, FieldOfficer];
}

/// <summary>Custom JWT claim types.</summary>
public static class AppClaimTypes
{
    /// <summary>One claim per farm the user is assigned to (farm ID).</summary>
    public const string Farm = "farm";
    public const string FullName = "full_name";
}
