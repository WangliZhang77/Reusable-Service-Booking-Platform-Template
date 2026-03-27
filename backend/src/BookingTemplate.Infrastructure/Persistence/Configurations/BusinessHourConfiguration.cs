using BookingTemplate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTemplate.Infrastructure.Persistence.Configurations;

public sealed class BusinessHourConfiguration : IEntityTypeConfiguration<BusinessHour>
{
    public void Configure(EntityTypeBuilder<BusinessHour> builder)
    {
        builder.ToTable("business_hours", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_business_hours_weekday_range", "\"Weekday\" BETWEEN 0 AND 6");
            tableBuilder.HasCheckConstraint(
                "ck_business_hours_open_close",
                "((\"IsOpen\" = FALSE AND \"OpenTime\" IS NULL AND \"CloseTime\" IS NULL) OR " +
                "(\"IsOpen\" = TRUE AND \"OpenTime\" IS NOT NULL AND \"CloseTime\" IS NOT NULL AND \"CloseTime\" > \"OpenTime\"))");
        });
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Weekday).IsRequired();
        builder.Property(x => x.IsOpen).HasDefaultValue(true);
        builder.Property(x => x.SlotIntervalMinutes).HasDefaultValue(30);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.Weekday).IsUnique();
    }
}
