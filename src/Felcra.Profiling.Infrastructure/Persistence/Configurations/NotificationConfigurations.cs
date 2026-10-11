using Felcra.Profiling.Domain.Notifications;
using Felcra.Profiling.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Felcra.Profiling.Infrastructure.Persistence.Configurations;

internal sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> b)
    {
        b.Property(x => x.Endpoint).HasMaxLength(1000);
        b.Property(x => x.P256dh).HasMaxLength(200);
        b.Property(x => x.Auth).HasMaxLength(100);
        b.Property(x => x.UserAgent).HasMaxLength(300);
        b.HasIndex(x => x.Endpoint).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReminderNotificationRunConfiguration : IEntityTypeConfiguration<ReminderNotificationRun>
{
    public void Configure(EntityTypeBuilder<ReminderNotificationRun> b) => b.HasIndex(x => x.Date).IsUnique();
}
