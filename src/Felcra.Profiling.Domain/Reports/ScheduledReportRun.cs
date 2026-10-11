using Felcra.Profiling.Domain.Common;

namespace Felcra.Profiling.Domain.Reports;

/// <summary>
/// One sending of the monthly report e-mail. The period is unique, so the reports for a month go out
/// once even if the server restarts or runs more than one instance.
/// </summary>
public class ScheduledReportRun : Entity
{
    /// <summary>First day of the reported month.</summary>
    public DateOnly Period { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int EmailsSent { get; set; }
}
