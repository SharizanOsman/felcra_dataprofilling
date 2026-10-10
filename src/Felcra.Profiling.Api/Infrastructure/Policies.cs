namespace Felcra.Profiling.Api.Infrastructure;

internal static class Policies
{
    /// <summary>HQ administrators only.</summary>
    public const string HqAdmin = nameof(HqAdmin);

    /// <summary>HQ administrators and farm managers.</summary>
    public const string Manager = nameof(Manager);
}
