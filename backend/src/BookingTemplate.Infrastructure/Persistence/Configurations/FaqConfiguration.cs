using BookingTemplate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BookingTemplate.Infrastructure.Persistence.Configurations;

public sealed class FaqConfiguration : IEntityTypeConfiguration<Faq>
{
    public void Configure(EntityTypeBuilder<Faq> builder)
    {
        builder.ToTable("faqs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Question).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Answer).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(80);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.IsPublished).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.IsPublished, x.SortOrder });
        builder.HasIndex(x => x.Category);
    }
}
