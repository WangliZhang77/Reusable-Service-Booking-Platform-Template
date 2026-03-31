using BookingTemplate.Application.DTOs.Chat;

namespace BookingTemplate.Application.Interfaces.Services;

/// <summary>
/// Gemini function-calling 对话（不处理 FAQ/价格本地短路，由编排层负责）。
/// </summary>
public interface IGeminiChatService
{
    Task<ChatResponseDto?> ReplyWithGeminiAndToolsAsync(string message, CancellationToken cancellationToken);
}
