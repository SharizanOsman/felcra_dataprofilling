namespace Felcra.Profiling.Contracts.Reports;

/// <summary>One sending of the monthly report e-mail.</summary>
public sealed record MonthlyReportRunDto(DateOnly Period, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int EmailsSent);

/// <summary>Monthly report e-mail settings and history, shown to HQ.</summary>
public sealed record MonthlyReportStatusDto(
    bool Enabled,
    bool EmailConfigured,
    int DayOfMonth,
    int Hour,
    int Recipients,
    IReadOnlyList<MonthlyReportRunDto> RecentRuns);

public sealed record TestEmailResultDto(string SentTo, IReadOnlyList<string> Attachments);
