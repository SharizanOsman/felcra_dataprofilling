using Felcra.Profiling.Domain.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Felcra.Profiling.Infrastructure.Persistence.Configurations;

internal sealed class ScheduledReportRunConfiguration : IEntityTypeConfiguration<ScheduledReportRun>
{
    public void Configure(EntityTypeBuilder<ScheduledReportRun> b)
    {
        b.HasIndex(x => x.Period).IsUnique();
    }
}
