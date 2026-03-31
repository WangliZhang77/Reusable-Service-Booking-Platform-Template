using BookingTemplate.Application.DTOs.Chat;
using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Application.Interfaces.Services;

namespace BookingTemplate.Application.Services;

/// <summary>
/// 聊天编排：先本地 FAQ / 服务与价格，再交给 Gemini + 工具。
/// </summary>
public sealed class ChatOrchestratorService(
    IBookingDataAccess dataAccess,
    IGeminiChatService geminiChat) : IChatService
{
    public async Task<ChatResponseDto> ReplyAsync(ChatRequestDto request, CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message is required.");
        }

        var intent = DetectIntent(message);

        var fromFaq = await TryAnswerFromFaqAsync(message, intent, cancellationToken);
        if (fromFaq is not null)
        {
            return fromFaq;
        }

        var fromService = await TryAnswerFromServiceAsync(message, intent, cancellationToken);
        if (fromService is not null)
        {
            return fromService;
        }

        var geminiResult = await geminiChat.ReplyWithGeminiAndToolsAsync(message, cancellationToken);
        if (!string.IsNullOrWhiteSpace(geminiResult?.Reply))
        {
            return geminiResult!;
        }

        return new ChatResponseDto(
            "I could not find a direct answer yet. Please check our FAQ page or contact us for help.",
            "fallback");
    }

    private async Task<ChatResponseDto?> TryAnswerFromFaqAsync(string message, string intent, CancellationToken cancellationToken)
    {
        List<Domain.Entities.Faq> faqs;
        try
        {
            faqs = await dataAccess.GetPublishedFaqsAsync(cancellationToken);
        }
        catch
        {
            return null;
        }

        var normalized = message.ToLowerInvariant();

        var direct = faqs.FirstOrDefault(x =>
            normalized.Contains(x.Question.ToLowerInvariant()) ||
            x.Question.ToLowerInvariant().Contains(normalized));

        if (direct is null)
        {
            var terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            direct = faqs.FirstOrDefault(x => terms.Any(t => t.Length > 2 && x.Question.ToLowerInvariant().Contains(t)));
        }

        if (direct is null)
        {
            return null;
        }

        return new ChatResponseDto(direct.Answer, intent == "general" ? "faq" : intent);
    }

    private async Task<ChatResponseDto?> TryAnswerFromServiceAsync(string message, string intent, CancellationToken cancellationToken)
    {
        List<Domain.Entities.Service> services;
        try
        {
            services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        }
        catch
        {
            return null;
        }

        var normalized = message.ToLowerInvariant();
        var matched = services.FirstOrDefault(x => normalized.Contains(x.Name.ToLowerInvariant()));

        if (matched is null)
        {
            return null;
        }

        // 预约/查空位交给 Gemini + 工具，避免仅因提到服务名就返回固定介绍。
        if (intent is "booking" or "availability")
        {
            return null;
        }

        if (intent == "price" || normalized.Contains("price") || normalized.Contains("cost"))
        {
            return new ChatResponseDto(
                $"{matched.Name} is currently priced at {matched.Price:C}. The service duration is about {matched.DurationMinutes} minutes.",
                "price");
        }

        return new ChatResponseDto(
            $"{matched.Name} takes around {matched.DurationMinutes} minutes. You can book it directly on the Booking page.",
            "service");
    }

    private static string DetectIntent(string message)
    {
        var normalized = message.ToLowerInvariant();
        if (normalized.Contains("price") || normalized.Contains("cost") || normalized.Contains("多少钱") || normalized.Contains("价格"))
        {
            return "price";
        }

        if (normalized.Contains("available") || normalized.Contains("availability") || normalized.Contains("time slot") || normalized.Contains("有空"))
        {
            return "availability";
        }

        if (normalized.Contains("book") || normalized.Contains("booking") || normalized.Contains("预约"))
        {
            return "booking";
        }

        if (normalized.Contains("faq") || normalized.Contains("question") || normalized.Contains("常见问题"))
        {
            return "faq";
        }

        return "general";
    }
}
