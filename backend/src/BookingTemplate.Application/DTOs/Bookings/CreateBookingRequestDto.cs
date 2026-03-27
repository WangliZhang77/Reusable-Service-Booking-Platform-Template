using BookingTemplate.Domain.Enums;

namespace BookingTemplate.Application.DTOs.Bookings;

public sealed class CreateBookingRequestDto
{
    public Guid ServiceId { get; set; }
    public DateOnly BookingDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public string? CustomerMessage { get; set; }
    public CustomerInputDto Customer { get; set; } = new();
    public PetInputDto Pet { get; set; } = new();
}

public sealed class CustomerInputDto
{
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class PetInputDto
{
    public string Name { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public string? Breed { get; set; }
    public PetSize Size { get; set; } = PetSize.Small;
    public string? SpecialNotes { get; set; }
}
