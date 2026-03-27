using BookingTemplate.Application.DTOs.Common;

namespace BookingTemplate.Application.DTOs.Bookings;

public sealed class UpdateBookingStatusRequestDto
{
    public BookingStatusDto Status { get; set; }
    public string? AdminNotes { get; set; }
    public string? CancelReason { get; set; }
}
