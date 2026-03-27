namespace BookingTemplate.Application.DTOs.Faq;

public sealed record FaqDto(
    Guid Id,
    string Question,
    string Answer,
    string? Category,
    int SortOrder,
    bool IsPublished
);
