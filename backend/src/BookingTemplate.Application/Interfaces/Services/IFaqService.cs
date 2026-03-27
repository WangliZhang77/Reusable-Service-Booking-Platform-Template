using BookingTemplate.Application.DTOs.Faq;

namespace BookingTemplate.Application.Interfaces.Services;

public interface IFaqService
{
    Task<IReadOnlyList<FaqDto>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FaqDto>> GetPublishedAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FaqDto>> GetByCategoryAsync(string category, CancellationToken cancellationToken);
}
