using BookingTemplate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTemplate.Infrastructure.Persistence.Configurations;

public sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_services_duration_range", "\"DurationMinutes\" > 0 AND \"DurationMinutes\" <= 480");
            tableBuilder.HasCheckConstraint("ck_services_price_non_negative", "\"Price\" >= 0");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description);
        builder.Property(x => x.DurationMinutes).IsRequired();
        builder.Property(x => x.Price).HasPrecision(10, 2).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
