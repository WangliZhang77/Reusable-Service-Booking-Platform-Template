using BookingTemplate.Application.DTOs.Chat;

namespace BookingTemplate.Application.Interfaces.Services;

public interface IChatService
{
    Task<ChatResponseDto> ReplyAsync(ChatRequestDto request, CancellationToken cancellationToken);
}
