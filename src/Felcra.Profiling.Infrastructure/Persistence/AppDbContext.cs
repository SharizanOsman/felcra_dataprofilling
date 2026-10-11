using System.Reflection;
using Felcra.Profiling.Application.Abstractions;
using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Auditing;
using Felcra.Profiling.Domain.Breeding;
using Felcra.Profiling.Domain.Census;
using Felcra.Profiling.Domain.Common;
using Felcra.Profiling.Domain.Farms;
using Felcra.Profiling.Domain.MasterData;
using Felcra.Profiling.Domain.Notifications;
using Felcra.Profiling.Domain.Records;
using Felcra.Profiling.Domain.Reports;
using Felcra.Profiling.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Felcra.Profiling.Infrastructure.Persistence;

public static class QueryFilters
{
    public const string SoftDelete = "SoftDelete";
    public const string FarmScope = "FarmScope";
}

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser? currentUser = null)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Farm> Farms => Set<Farm>();
    public DbSet<FarmProject> FarmProjects => Set<FarmProject>();
    public DbSet<HerdGroup> HerdGroups => Set<HerdGroup>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Breed> Breeds => Set<Breed>();
    public DbSet<TreatmentProduct> TreatmentProducts => Set<TreatmentProduct>();
    public DbSet<FeedIngredient> FeedIngredients => Set<FeedIngredient>();
    public DbSet<Animal> Animals => Set<Animal>();
    public DbSet<AnimalTag> AnimalTags => Set<AnimalTag>();
    public DbSet<CensusSession> CensusSessions => Set<CensusSession>();
    public DbSet<CensusSessionTreatment> CensusSessionTreatments => Set<CensusSessionTreatment>();
    public DbSet<CensusEntry> CensusEntries => Set<CensusEntry>();
    public DbSet<BreedingEvent> BreedingEvents => Set<BreedingEvent>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<TreatmentRecord> TreatmentRecords => Set<TreatmentRecord>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<UserFarm> UserFarms => Set<UserFarm>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ScheduledReportRun> ScheduledReportRuns => Set<ScheduledReportRun>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<ReminderNotificationRun> ReminderNotificationRuns => Set<ReminderNotificationRun>();

    // Evaluated per context instance, so query filters below are parameterised per request.
    // No authenticated user (background jobs, seeding, migrations) or HQ admin => no farm restriction.
    // Anonymous HTTP access to data is prevented by the API's fallback authorization policy.
    internal bool BypassFarmScope => currentUser is not { IsAuthenticated: true } || currentUser.IsHqAdmin;
    internal Guid[] ScopedFarmIds => currentUser?.FarmIds.ToArray() ?? [];

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<Farm>().HasQueryFilter(QueryFilters.FarmScope, f => BypassFarmScope || ScopedFarmIds.Contains(f.Id));

        foreach (var clrType in builder.Model.GetEntityTypes().Select(t => t.ClrType).ToList())
        {
            // IDs are generated in code (UUIDv7, possibly offline on a device). Without this, EF treats a
            // new child added to a tracked parent (e.g. a tag-history row) as existing and issues an UPDATE.
            if (typeof(Entity).IsAssignableFrom(clrType))
                builder.Entity(clrType).Property(nameof(Entity.Id)).ValueGeneratedNever();

            // Npgsql maps a uint row version to PostgreSQL's system column xmin.
            if (typeof(AuditableEntity).IsAssignableFrom(clrType))
                builder.Entity(clrType).Property(nameof(AuditableEntity.Version)).IsRowVersion();

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
                SoftDeleteFilterMethod.MakeGenericMethod(clrType).Invoke(this, [builder]);
            if (typeof(IFarmScoped).IsAssignableFrom(clrType))
                FarmScopeFilterMethod.MakeGenericMethod(clrType).Invoke(this, [builder]);
        }
    }

    private static readonly MethodInfo SoftDeleteFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplySoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo FarmScopeFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyFarmScopeFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void ApplySoftDeleteFilter<TEntity>(ModelBuilder builder) where TEntity : class, ISoftDeletable =>
        builder.Entity<TEntity>().HasQueryFilter(QueryFilters.SoftDelete, e => !e.IsDeleted);

    private void ApplyFarmScopeFilter<TEntity>(ModelBuilder builder) where TEntity : class, IFarmScoped =>
        builder.Entity<TEntity>().HasQueryFilter(QueryFilters.FarmScope, e => BypassFarmScope || ScopedFarmIds.Contains(e.FarmId));
}
