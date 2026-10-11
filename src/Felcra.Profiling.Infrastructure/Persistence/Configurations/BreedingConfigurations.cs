using Felcra.Profiling.Domain.Animals;
using Felcra.Profiling.Domain.Breeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Felcra.Profiling.Infrastructure.Persistence.Configurations;

internal sealed class BreedingEventConfiguration : IEntityTypeConfiguration<BreedingEvent>
{
    public void Configure(EntityTypeBuilder<BreedingEvent> b)
    {
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.PregnancyResult).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.SoundnessResult).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.SemenCode).HasMaxLength(50);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.AnimalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Animal>().WithMany().HasForeignKey(x => x.SireId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.AnimalId, x.EventDate });
    }
}
