using Felcra.Profiling.Domain.Animals;
using MudBlazor;

namespace Felcra.Profiling.UI.Shared.Forms;

/// <summary>Display text for animal values.</summary>
public static class AnimalText
{
    /// <summary>"Dam (I)" — English name with the census-form code.</summary>
    public static string Category(AnimalCategory category) => category switch
    {
        AnimalCategory.Bull => "Bull (P)",
        AnimalCategory.Dam => "Dam (I)",
        AnimalCategory.MaleCalf => "Male calf (AJ)",
        AnimalCategory.FemaleCalf => "Female calf (AB)",
        _ => category.ToString(),
    };

    public static string Status(AnimalStatus status) => EnumText.Humanize(status);

    public static Color StatusColor(AnimalStatus status) => status switch
    {
        AnimalStatus.Active => Color.Success,
        AnimalStatus.Sold or AnimalStatus.Transferred => Color.Info,
        _ => Color.Default,
    };

    public static string Months(int? months) => months switch
    {
        null => "",
        < 12 => $"{months} mo",
        _ when months % 12 == 0 => $"{months / 12} yr",
        _ => $"{months / 12} yr {months % 12} mo",
    };

    /// <summary>Current age from the birth date, marked "~" when estimated.</summary>
    public static string Age(DateOnly? birthDate, AgeEstimationMethod? method, DateOnly today)
    {
        if (birthDate is not { } birth) return "";
        var months = Math.Max(0, (today.Year - birth.Year) * 12 + today.Month - birth.Month - (today.Day < birth.Day ? 1 : 0));
        return (method is AgeEstimationMethod.Recorded ? "" : "~") + Months(months);
    }

    public static string Weight(decimal? kg) => kg is null ? "" : $"{kg:0.#} kg";
}
