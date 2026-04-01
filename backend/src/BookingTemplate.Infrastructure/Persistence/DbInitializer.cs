using BookingTemplate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingTemplate.Infrastructure.Persistence;

/// <summary>
/// 开发/演示用最小数据集：至少 2 个服务、一周营业时间、若干 FAQ。
/// 各表独立补种，避免 "Services 已有但 FAQs 为空" 的情况。
/// </summary>
public static class DbInitializer
{
    public static readonly Guid ServiceFullGroomId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid ServiceBathTidyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task SeedDemoDataIfEmptyAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (!await db.Services.AnyAsync(cancellationToken))
        {
            db.Services.AddRange(
                new Service
                {
                    Id = ServiceFullGroomId,
                    Name = "Full Groom",
                    Description = "Full bath, haircut, nail trim, ear cleaning.",
                    DurationMinutes = 90,
                    Price = 89.00m,
                    IsActive = true,
                    SortOrder = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new Service
                {
                    Id = ServiceBathTidyId,
                    Name = "Bath & Tidy",
                    Description = "Bath and light brush-out.",
                    DurationMinutes = 45,
                    Price = 49.00m,
                    IsActive = true,
                    SortOrder = 2,
                    CreatedAt = now,
                    UpdatedAt = now
                });
        }

        if (!await db.BusinessHours.AnyAsync(cancellationToken))
        {
            for (short weekday = 0; weekday <= 6; weekday++)
            {
                db.BusinessHours.Add(new BusinessHour
                {
                    Id = Guid.NewGuid(),
                    Weekday = weekday,
                    IsOpen = true,
                    OpenTime = new TimeOnly(9, 0),
                    CloseTime = new TimeOnly(17, 0),
                    SlotIntervalMinutes = 30,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        if (!await db.Faqs.AnyAsync(cancellationToken))
        {
            db.Faqs.AddRange(
                new Faq
                {
                    Id = Guid.NewGuid(),
                    Question = "What are your opening hours?",
                    Answer = "We are open 9:00-17:00, seven days a week (demo data).",
                    Category = "Hours",
                    SortOrder = 1,
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new Faq
                {
                    Id = Guid.NewGuid(),
                    Question = "How do I cancel a booking?",
                    Answer = "Contact us at least 24 hours in advance. Phone cancellations are accepted during business hours.",
                    Category = "Policy",
                    SortOrder = 2,
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new Faq
                {
                    Id = Guid.NewGuid(),
                    Question = "Do you groom cats?",
                    Answer = "Yes, we groom both dogs and cats. Please mention your pet type when booking.",
                    Category = "Services",
                    SortOrder = 3,
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new Faq
                {
                    Id = Guid.NewGuid(),
                    Question = "What if I am late?",
                    Answer = "Please call ahead. We may need to reschedule if you are more than 15 minutes late.",
                    Category = "Policy",
                    SortOrder = 4,
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new Faq
                {
                    Id = Guid.NewGuid(),
                    Question = "What vaccines are required?",
                    Answer = "Please bring current vaccination records. Requirements vary by region-ask at check-in.",
                    Category = "Health",
                    SortOrder = 5,
                    IsPublished = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
