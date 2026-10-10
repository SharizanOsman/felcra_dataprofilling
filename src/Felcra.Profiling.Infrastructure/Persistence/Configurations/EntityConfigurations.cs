using Felcra.Profiling.Domain.Auditing;
using Felcra.Profiling.Domain.Farms;
using Felcra.Profiling.Domain.MasterData;
using Felcra.Profiling.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Felcra.Profiling.Infrastructure.Persistence.Configurations;

internal sealed class FarmConfiguration : IEntityTypeConfiguration<Farm>
{
    public void Configure(EntityTypeBuilder<Farm> b)
    {
        b.Property(x => x.Code).HasMaxLength(32);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.State).HasMaxLength(100);
        b.Property(x => x.AreaHectares).HasPrecision(10, 2);
        b.HasIndex(x => x.Code).IsUnique().HasFilter(NotDeleted);
        b.HasMany(x => x.Projects).WithOne().HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>Unique indexes ignore soft-deleted rows so a code can be reused after deletion.</summary>
    internal const string NotDeleted = "is_deleted = false";
}

internal sealed class FarmProjectConfiguration : IEntityTypeConfiguration<FarmProject>
{
    public void Configure(EntityTypeBuilder<FarmProject> b)
    {
        b.HasKey(x => new { x.FarmId, x.ProjectId });
        b.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        // Hide links to soft-deleted projects (matches the Project soft-delete filter).
        b.HasQueryFilter(QueryFilters.SoftDelete, x => !x.Project!.IsDeleted);
    }
}

internal sealed class HerdGroupConfiguration : IEntityTypeConfiguration<HerdGroup>
{
    public void Configure(EntityTypeBuilder<HerdGroup> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasOne<Farm>().WithMany().HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.FarmId, x.Name }).IsUnique().HasFilter(FarmConfiguration.NotDeleted);
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.Property(x => x.Code).HasMaxLength(32);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.PlanReference).HasMaxLength(50);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => x.Code).IsUnique().HasFilter(FarmConfiguration.NotDeleted);
    }
}

internal sealed class BreedConfiguration : IEntityTypeConfiguration<Breed>
{
    public void Configure(EntityTypeBuilder<Breed> b)
    {
        b.Property(x => x.Code).HasMaxLength(32);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => x.Code).IsUnique().HasFilter(FarmConfiguration.NotDeleted);
    }
}

internal sealed class TreatmentProductConfiguration : IEntityTypeConfiguration<TreatmentProduct>
{
    public void Configure(EntityTypeBuilder<TreatmentProduct> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.DefaultDose).HasPrecision(10, 3);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => x.Name).IsUnique().HasFilter(FarmConfiguration.NotDeleted);
    }
}

internal sealed class FeedIngredientConfiguration : IEntityTypeConfiguration<FeedIngredient>
{
    public void Configure(EntityTypeBuilder<FeedIngredient> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
        foreach (var p in new[] { nameof(FeedIngredient.DryMatterPercent), nameof(FeedIngredient.CrudeProteinPercent),
                     nameof(FeedIngredient.TdnPercent), nameof(FeedIngredient.CalciumPercent), nameof(FeedIngredient.PhosphorusPercent) })
            b.Property(p).HasPrecision(6, 3);
        b.Property(x => x.MetabolisableEnergy).HasPrecision(6, 3);
        b.Property(x => x.PricePerKg).HasPrecision(10, 4);
        b.HasIndex(x => x.Name).IsUnique().HasFilter(FarmConfiguration.NotDeleted);
    }
}

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200);
        b.HasMany(x => x.Farms).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserFarmConfiguration : IEntityTypeConfiguration<UserFarm>
{
    public void Configure(EntityTypeBuilder<UserFarm> b)
    {
        b.HasKey(x => new { x.UserId, x.FarmId });
        b.HasOne(x => x.Farm).WithMany().HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.EntityType).HasMaxLength(100);
        b.Property(x => x.UserName).HasMaxLength(256);
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Changes).HasColumnType("jsonb");
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.Timestamp);
    }
}
