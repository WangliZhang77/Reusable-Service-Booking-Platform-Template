using BookingTemplate.Domain.Enums;

namespace BookingTemplate.Domain.Entities;

public sealed class Booking
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid PetId { get; set; }
    public DateOnly BookingDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string? CustomerMessage { get; set; }
    public string? AdminNotes { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Service Service { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Pet Pet { get; set; } = null!;
}
