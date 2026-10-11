using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Census;
using Felcra.Profiling.Domain.Farms;
using Felcra.Profiling.Domain.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Felcra.Profiling.Infrastructure.Persistence.Configurations;

internal sealed class AnimalConfiguration : IEntityTypeConfiguration<Animal>
{
    public void Configure(EntityTypeBuilder<Animal> b)
    {
        b.Property(x => x.AnimalNo).UseIdentityAlwaysColumn()
            .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        b.HasIndex(x => x.AnimalNo).IsUnique();

        // Tag numbers are deliberately not unique: real herds have duplicates and untagged animals.
        b.Property(x => x.TagNumber).HasMaxLength(32);
        b.HasIndex(x => new { x.FarmId, x.TagNumber });

        b.Property(x => x.Sex).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.BirthDateMethod).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.StatusReason).HasMaxLength(500);
        b.Property(x => x.Remarks).HasMaxLength(1000);

        b.HasOne<Farm>().WithMany().HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HerdGroup>().WithMany().HasForeignKey(x => x.HerdGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Breed>().WithMany().HasForeignKey(x => x.BreedId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.DamId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.SireId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.TagHistory).WithOne().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AnimalTagConfiguration : IEntityTypeConfiguration<AnimalTag>
{
    public void Configure(EntityTypeBuilder<AnimalTag> b)
    {
        b.Property(x => x.TagNumber).HasMaxLength(32);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => x.TagNumber);
    }
}

internal sealed class CensusSessionConfiguration : IEntityTypeConfiguration<CensusSession>
{
    public void Configure(EntityTypeBuilder<CensusSession> b)
    {
        b.Property(x => x.Title).HasMaxLength(200);
        b.Property(x => x.PicName).HasMaxLength(200);
        b.Property(x => x.PicPosition).HasMaxLength(200);
        b.Property(x => x.PicPhone).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<Farm>().WithMany().HasForeignKey(x => x.FarmId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Treatments).WithOne().HasForeignKey(x => x.CensusSessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.FarmId, x.StartDate });
    }
}

internal sealed class CensusSessionTreatmentConfiguration : IEntityTypeConfiguration<CensusSessionTreatment>
{
    public void Configure(EntityTypeBuilder<CensusSessionTreatment> b)
    {
        b.HasKey(x => new { x.CensusSessionId, x.TreatmentProductId });
        b.HasOne<TreatmentProduct>().WithMany().HasForeignKey(x => x.TreatmentProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CensusEntryConfiguration : IEntityTypeConfiguration<CensusEntry>
{
    public void Configure(EntityTypeBuilder<CensusEntry> b)
    {
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.WeightKg).HasPrecision(7, 2);
        b.Property(x => x.BodyConditionScore).HasPrecision(3, 1);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.HasOne<CensusSession>().WithMany().HasForeignKey(x => x.CensusSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CensusSessionId, x.AnimalId }).IsUnique().HasFilter(FarmConfiguration.NotDeleted);
        b.HasIndex(x => x.AnimalId);
    }
}
