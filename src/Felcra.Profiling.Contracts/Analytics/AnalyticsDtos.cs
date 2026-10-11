using Felcra.Profiling.Contracts.Census;
using Felcra.Profiling.Domain.Animals;

namespace Felcra.Profiling.Contracts.Analytics;

// ---- Growth (Gompertz) -------------------------------------------------------------------------

public sealed record GompertzDto(
    double MatureWeightKg, double B, double K, double BirthWeightKg, double InflectionAgeDays,
    double RSquared, double RmseKg, int Points);

public sealed record CurvePointDto(double AgeMonths, double WeightKg, double DailyGainKg);

/// <summary>An animal's latest weighing compared with the herd growth curve.</summary>
public sealed record GrowthAnimalDto(
    Guid AnimalId, string AnimalNo, string? TagNumber, AnimalCategory Category, bool AgeIsEstimated,
    int AgeDays, decimal WeightKg, DateOnly WeighedOn, double? ExpectedKg, double? DeviationPercent);

public sealed record GrowthCurveDto(Sex Sex, GompertzDto? Fit, string? Message, IReadOnlyList<CurvePointDto> Curve, IReadOnlyList<GrowthAnimalDto> Animals);

public sealed record GrowthAnalysisDto(
    IReadOnlyList<GrowthCurveDto> Curves,
    IReadOnlyList<GrowthAnimalDto> BelowCurve,
    int WeighingsUsed,
    int AnimalsWithoutBirthDate,
    double BelowCurveThresholdPercent);

// ---- Lactation (Ali-Schaeffer) -----------------------------------------------------------------

public sealed record AliSchaefferDto(double A, double B, double C, double D, double E, double RSquared, int Points,
    double Total305Litres, int PeakDay, double PeakLitres);

public sealed record LactationAnimalDto(Guid AnimalId, string AnimalNo, string? TagNumber, int TestDays, AliSchaefferDto? Fit);

public sealed record LactationAnalysisDto(
    int MilkRecords,
    int RecordsWithoutCalving,
    AliSchaefferDto? HerdFit,
    string? Message,
    IReadOnlyList<CurvePointDto> HerdCurve,
    IReadOnlyList<LactationAnimalDto> Animals);

// ---- Dashboard -----------------------------------------------------------------------------------

public sealed record CategoryStatDto(AnimalCategory Category, int Count, decimal? AverageWeightKg, decimal? AverageBcs);

public sealed record WeeklyActivityDto(DateOnly WeekStart, int Weighings, int Treatments, int BreedingEvents, int Observations);

public sealed record DashboardDto(
    int ActiveAnimals,
    int? Capacity,
    CategoryCounts ByCategory,
    IReadOnlyList<CategoryStatDto> Categories,
    int Untagged,
    int SharingATag,
    int WeighedLast90Days,
    decimal? PregnancyRatePercent,
    int BreedingFemalesWithoutRecords,
    int TasksDue,
    int TasksOverdue,
    int CullingCandidates,
    int TreatmentsLast30Days,
    int CalvesLast12Months,
    int DeathsLast12Months,
    /// <summary>Deaths in 12 months as % of the herd (active animals plus those deaths).</summary>
    decimal? MortalityPercent,
    IReadOnlyList<WeeklyActivityDto> Activity);
