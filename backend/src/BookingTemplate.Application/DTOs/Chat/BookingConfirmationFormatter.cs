using System.Text.Json;

namespace BookingTemplate.Application.DTOs.Chat;

public static class BookingConfirmationFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public static ChatResponseDto ToResponse(BookingConfirmationPayload pending)
    {
        var token = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(pending, JsonOptions));
        var reply =
            "Wonderful — here is everything I have for your appointment. Please give it a quick look:\n\n" +
            "I have prepared your booking details:\n" +
            $"Service: {pending.ServiceName}\n" +
            $"Date: {pending.Date}\n" +
            $"Time: {(string.IsNullOrWhiteSpace(pending.StartTime) ? "We will assign the nearest available slot." : pending.StartTime)}\n" +
            $"Pet: {pending.PetName}{(string.IsNullOrWhiteSpace(pending.PetType) ? string.Empty : $" ({pending.PetType})")}\n" +
            $"Customer: {(string.IsNullOrWhiteSpace(pending.CustomerName) ? "null" : pending.CustomerName)}\n" +
            $"Phone: {pending.Phone}\n\n" +
            "If everything looks good, reply exactly:\n" +
            $"Yes, confirm {token}";
        return new ChatResponseDto(reply, "booking_confirmation");
    }
}
