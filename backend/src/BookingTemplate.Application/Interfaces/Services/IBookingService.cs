using BookingTemplate.Application.DTOs.Availability;
using BookingTemplate.Application.DTOs.Bookings;
using BookingTemplate.Application.DTOs.Common;
using BookingTemplate.Application.DTOs.Services;

namespace BookingTemplate.Application.Interfaces.Services;

public interface IBookingService
{
    Task<IReadOnlyList<ServiceDto>> GetServicesAsync(CancellationToken cancellationToken);
    Task<AvailabilityResponseDto> GetAvailabilityAsync(Guid serviceId, DateOnly date, CancellationToken cancellationToken);
    Task<BookingDto> CreateBookingAsync(CreateBookingRequestDto request, CancellationToken cancellationToken);
    Task<IReadOnlyList<BookingDto>> GetBookingsAsync(DateOnly? date, CancellationToken cancellationToken);
    Task<BookingDto> UpdateBookingStatusAsync(Guid bookingId, BookingStatusDto status, string? adminNotes, string? cancelReason, CancellationToken cancellationToken);
}
