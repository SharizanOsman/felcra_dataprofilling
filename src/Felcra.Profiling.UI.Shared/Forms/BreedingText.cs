using Felcra.Profiling.Contracts.Breeding;
using Felcra.Profiling.Domain.Breeding;
using MudBlazor;

namespace Felcra.Profiling.UI.Shared.Forms;

/// <summary>Display text for breeding values, shared by the admin portal and the field app.</summary>
public static class BreedingText
{
    public static string EventType(BreedingEventType type) => type switch
    {
        BreedingEventType.Insemination => "AI (insemination)",
        BreedingEventType.NaturalService => "Natural service",
        BreedingEventType.PregnancyCheck => "Pregnancy check",
        BreedingEventType.BullSoundnessExam => "Bull soundness exam",
        _ => EnumText.Humanize(type),
    };

    /// <summary>One-line summary, e.g. "Pregnancy check — pregnant (90 days)" or "Calving — 1 calf, assisted".</summary>
    public static string Summary(BreedingEventType type, PregnancyResult? result, int? gestationDays, int calves, int? stillborn, bool? assisted,
        SoundnessResult? soundness) => type switch
    {
        BreedingEventType.PregnancyCheck when result is { } r =>
            $"Pregnancy check — {EnumText.Humanize(r).ToLowerInvariant()}{(gestationDays is { } d ? $" ({d} days)" : "")}",
        BreedingEventType.Calving =>
            $"Calving — {calves} calf{(calves == 1 ? "" : "ves")}{(stillborn is > 0 ? $", {stillborn} stillborn" : "")}{(assisted == true ? ", assisted" : "")}",
        BreedingEventType.BullSoundnessExam when soundness is { } s => $"Bull soundness exam — {EnumText.Humanize(s).ToLowerInvariant()}",
        _ => EventType(type),
    };

    public static string Summary(BreedingEventDto e) =>
        Summary(e.Type, e.PregnancyResult, e.GestationDays, e.CalfIds.Count, e.StillbornCount, e.Assisted, e.SoundnessResult);

    /// <summary>"Pregnant · due 12 Mar 2027", "Served — check due 2 Nov", "Open", or null when unknown.</summary>
    public static string? Status(ReproductiveState? state, DateOnly? expectedCalving, DateOnly? checkDue = null) => state switch
    {
        ReproductiveState.Pregnant => expectedCalving is { } due ? $"Pregnant · calving due {due:dd MMM yyyy}" : "Pregnant",
        ReproductiveState.Served => checkDue is { } check ? $"Served · pregnancy check due {check:dd MMM yyyy}" : "Served · waiting for pregnancy check",
        ReproductiveState.Open => "Open (not pregnant)",
        _ => null,
    };

    public static Color StateColor(ReproductiveState? state) => state switch
    {
        ReproductiveState.Pregnant => Color.Success,
        ReproductiveState.Served => Color.Info,
        ReproductiveState.Open => Color.Warning,
        _ => Color.Default,
    };

    public static string ReminderIcon(ReminderType type) => type switch
    {
        ReminderType.PregnancyCheckDue => Icons.Material.Filled.PregnantWoman,
        ReminderType.CalvingDue => Icons.Material.Filled.ChildFriendly,
        ReminderType.TreatmentDue => Icons.Material.Filled.Vaccines,
        _ => Icons.Material.Filled.MonitorHeart,
    };
}
