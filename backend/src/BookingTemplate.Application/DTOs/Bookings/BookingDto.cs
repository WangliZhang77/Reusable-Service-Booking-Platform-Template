using BookingTemplate.Application.DTOs.Common;

namespace BookingTemplate.Application.DTOs.Bookings;

public sealed record BookingDto(
    Guid Id,
    Guid ServiceId,
    string ServiceName,
    Guid CustomerId,
    string? CustomerName,
    string CustomerPhone,
    Guid PetId,
    string PetName,
    DateOnly BookingDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingStatusDto Status,
    string? CustomerMessage,
    string? AdminNotes,
    DateTimeOffset CreatedAt
);
