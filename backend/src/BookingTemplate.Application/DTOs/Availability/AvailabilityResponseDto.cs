namespace BookingTemplate.Application.DTOs.Availability;

public sealed record AvailabilityResponseDto(
    Guid ServiceId,
    DateOnly Date,
    IReadOnlyList<AvailabilitySlotDto> Slots
);
