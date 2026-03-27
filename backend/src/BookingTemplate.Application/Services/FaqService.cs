using BookingTemplate.Application.DTOs.Faq;
using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Application.Interfaces.Services;

namespace BookingTemplate.Application.Services;

public sealed class FaqService(IBookingDataAccess dataAccess) : IFaqService
{
    public async Task<IReadOnlyList<FaqDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var faqs = await dataAccess.GetAllFaqsAsync(cancellationToken);
        return faqs.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<FaqDto>> GetPublishedAsync(CancellationToken cancellationToken)
    {
        var faqs = await dataAccess.GetPublishedFaqsAsync(cancellationToken);
        return faqs.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<FaqDto>> GetByCategoryAsync(string category, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category is required.");
        }

        var faqs = await dataAccess.GetPublishedFaqsByCategoryAsync(category.Trim(), cancellationToken);
        return faqs.Select(Map).ToList();
    }

    private static FaqDto Map(Domain.Entities.Faq faq)
    {
        return new FaqDto(
            faq.Id,
            faq.Question,
            faq.Answer,
            faq.Category,
            faq.SortOrder,
            faq.IsPublished);
    }
}
