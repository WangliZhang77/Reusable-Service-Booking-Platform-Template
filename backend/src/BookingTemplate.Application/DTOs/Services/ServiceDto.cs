namespace BookingTemplate.Application.DTOs.Services;

public sealed record ServiceDto(
    Guid Id,
    string Name,
    string? Description,
    int DurationMinutes,
    decimal Price
);
