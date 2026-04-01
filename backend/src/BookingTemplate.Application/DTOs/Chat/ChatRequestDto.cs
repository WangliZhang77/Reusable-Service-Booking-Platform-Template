namespace BookingTemplate.Application.DTOs.Chat;

public sealed class ChatRequestDto
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}
