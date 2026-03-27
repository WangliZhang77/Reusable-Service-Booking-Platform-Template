using BookingTemplate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTemplate.Infrastructure.Persistence.Configurations;

public sealed class PetConfiguration : IEntityTypeConfiguration<Pet>
{
    public void Configure(EntityTypeBuilder<Pet> builder)
    {
        builder.ToTable("pets", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_pets_weight_range", "\"WeightKg\" IS NULL OR (\"WeightKg\" > 0 AND \"WeightKg\" <= 150)");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Species).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Breed).HasMaxLength(80);
        builder.Property(x => x.WeightKg).HasPrecision(5, 2);
        builder.Property(x => x.SpecialNotes);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Pets)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CustomerId);
    }
}
