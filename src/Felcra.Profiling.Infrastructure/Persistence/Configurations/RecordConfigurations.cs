using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Census;
using Felcra.Profiling.Domain.MasterData;
using Felcra.Profiling.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Felcra.Profiling.Infrastructure.Persistence.Configurations;

internal sealed class MeasurementConfiguration : IEntityTypeConfiguration<Measurement>
{
    public void Configure(EntityTypeBuilder<Measurement> b)
    {
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Value).HasPrecision(7, 2);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AnimalId, x.Type, x.MeasuredOn });
    }
}

internal sealed class TreatmentRecordConfiguration : IEntityTypeConfiguration<TreatmentRecord>
{
    public void Configure(EntityTypeBuilder<TreatmentRecord> b)
    {
        b.Property(x => x.Dose).HasPrecision(10, 3);
        b.Property(x => x.DoseUnit).HasMaxLength(20);
        b.Property(x => x.Route).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.BatchNumber).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TreatmentProduct>().WithMany().HasForeignKey(x => x.TreatmentProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CensusSession>().WithMany().HasForeignKey(x => x.CensusSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AnimalId, x.GivenOn });
        // Upcoming boosters, for reminders in Step 6.
        b.HasIndex(x => x.NextDueOn);
    }
}

internal sealed class ObservationConfiguration : IEntityTypeConfiguration<Observation>
{
    public void Configure(EntityTypeBuilder<Observation> b)
    {
        b.Property(x => x.Category).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Photos).WithOne().HasForeignKey(x => x.ObservationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AnimalId, x.ObservedOn });
    }
}

internal sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> b)
    {
        b.Property(x => x.StorageKey).HasMaxLength(200);
        b.Property(x => x.ContentType).HasMaxLength(50);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Restrict);
    }
}
