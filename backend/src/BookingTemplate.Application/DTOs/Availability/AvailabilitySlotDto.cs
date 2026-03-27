namespace BookingTemplate.Application.DTOs.Availability;

public sealed record AvailabilitySlotDto(
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsAvailable
);
