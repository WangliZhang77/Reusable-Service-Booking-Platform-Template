using System.Net.Http.Json;
using System.Text.Json;
using BookingTemplate.Application.DTOs.Chat;
using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;

namespace BookingTemplate.Infrastructure.Services;

public sealed class GeminiChatService(
    IBookingDataAccess dataAccess,
    IBookingService bookingService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IChatService
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

        // Booking / availability intent: answer without Gemini when API key is missing or DB is unavailable.
        if (intent is "booking" or "availability")
        {
            return new ChatResponseDto(
                "To book, open the Booking page and choose a service, date, and time. " +
                "If you tell me the service name and date (YYYY-MM-DD), I can also check available slots when Gemini is configured.",
                intent);
        }

        var geminiResult = await TryAskGeminiWithToolsAsync(message, cancellationToken);
        if (!string.IsNullOrWhiteSpace(geminiResult.reply))
        {
            return new ChatResponseDto(geminiResult.reply!, geminiResult.intent ?? "general");
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

    private async Task<(string? reply, string? intent)> TryAskGeminiWithToolsAsync(string message, CancellationToken cancellationToken)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return (null, null);
        }

        var model = configuration["Gemini:Model"] ?? "gemini-2.5-flash";
        var endpoint =
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        var systemPrompt =
            "You are a helpful assistant for a local service booking website. " +
            "Prefer calling tools when user asks pricing, availability, faq, or booking actions. " +
            "Keep answers short and practical.";

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = $"{systemPrompt}\n\nUser message: {message}" }
                    }
                }
            },
            tools = new[]
            {
                new
                {
                    functionDeclarations = new object[]
                    {
                        new
                        {
                            name = "CheckAvailability",
                            description = "Check available booking slots for a service and date.",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    serviceName = new { type = "STRING" },
                                    date = new { type = "STRING", description = "YYYY-MM-DD" }
                                }
                            }
                        },
                        new
                        {
                            name = "CreateBooking",
                            description = "Create booking request guidance for service, date and customer details.",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    serviceName = new { type = "STRING" },
                                    date = new { type = "STRING" },
                                    startTime = new { type = "STRING" },
                                    customerName = new { type = "STRING" },
                                    phone = new { type = "STRING" }
                                }
                            }
                        },
                        new
                        {
                            name = "GetServicePrice",
                            description = "Get service price and duration by service name.",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    serviceName = new { type = "STRING" }
                                }
                            }
                        },
                        new
                        {
                            name = "SearchFaq",
                            description = "Search FAQ entries by query text.",
                            parameters = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    query = new { type = "STRING" }
                                }
                            }
                        }
                    }
                }
            }
        };

        var client = httpClientFactory.CreateClient();
        using var response = await client.PostAsJsonAsync(endpoint, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, null);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch
        {
            return (null, null);
        }

        using (doc)
        {

            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var first = candidates[0];
            if (!first.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
            {
                return (null, null);
            }

            for (var i = 0; i < parts.GetArrayLength(); i++)
            {
                var part = parts[i];
                if (part.TryGetProperty("functionCall", out var functionCall))
                {
                    var functionName = functionCall.TryGetProperty("name", out var nameElement)
                        ? nameElement.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(functionName))
                    {
                        continue;
                    }

                    var args = ResolveFunctionArgs(functionCall);

                    var toolReply = await ExecuteToolAsync(functionName!, args, cancellationToken);
                    return (toolReply, ToIntent(functionName!));
                }

                if (part.TryGetProperty("text", out var textElement))
                {
                    return (textElement.GetString(), "general");
                }
            }

            return (null, null);
        }
    }

    private static JsonElement ResolveFunctionArgs(JsonElement functionCall)
    {
        if (functionCall.TryGetProperty("args", out var args))
        {
            return args;
        }

        // Some API responses use "arguments" as a JSON string.
        if (functionCall.TryGetProperty("arguments", out var argumentsElement))
        {
            if (argumentsElement.ValueKind == JsonValueKind.String)
            {
                var raw = argumentsElement.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        using var parsed = JsonDocument.Parse(raw);
                        return parsed.RootElement.Clone();
                    }
                    catch
                    {
                        return default;
                    }
                }
            }

            if (argumentsElement.ValueKind == JsonValueKind.Object)
            {
                return argumentsElement;
            }
        }

        return default;
    }

    private async Task<string> ExecuteToolAsync(string functionName, JsonElement args, CancellationToken cancellationToken)
    {
        return functionName switch
        {
            "GetServicePrice" => await ExecuteGetServicePriceAsync(args, cancellationToken),
            "SearchFaq" => await ExecuteSearchFaqAsync(args, cancellationToken),
            "CheckAvailability" => await ExecuteCheckAvailabilityAsync(args, cancellationToken),
            "CreateBooking" => ExecuteCreateBooking(args),
            _ => "This tool is not available."
        };
    }

    private async Task<string> ExecuteGetServicePriceAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var serviceName = GetStringArg(args, "serviceName");
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return "Please provide a service name to check pricing.";
        }

        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service. Please share the exact service name.";
        }

        return $"{match.Name} costs {match.Price:C} and takes about {match.DurationMinutes} minutes.";
    }

    private async Task<string> ExecuteSearchFaqAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var query = GetStringArg(args, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Please tell me what you want to know.";
        }

        var faqs = await dataAccess.GetPublishedFaqsAsync(cancellationToken);
        var terms = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var matched = faqs
            .Where(f => terms.Any(t => t.Length > 2 &&
                                       (f.Question.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                        f.Answer.Contains(t, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(f => f.SortOrder)
            .Take(3)
            .ToList();

        if (matched.Count == 0)
        {
            return "I could not find a matching FAQ entry. Please rephrase your question.";
        }

        return string.Join("\n\n", matched.Select(x => $"Q: {x.Question}\nA: {x.Answer}"));
    }

    private async Task<string> ExecuteCheckAvailabilityAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var serviceName = GetStringArg(args, "serviceName");
        var dateText = GetStringArg(args, "date");

        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(dateText))
        {
            return "Please provide both serviceName and date (YYYY-MM-DD).";
        }

        if (!DateOnly.TryParse(dateText, out var date))
        {
            return "Date format is invalid. Use YYYY-MM-DD.";
        }

        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service. Please share the exact service name.";
        }

        Application.DTOs.Availability.AvailabilityResponseDto? availability;
        try
        {
            availability = await bookingService.GetAvailabilityAsync(match.Id, date, cancellationToken);
        }
        catch
        {
            return "I could not check availability right now. Please try again.";
        }

        var topSlots = availability.Slots
            .Where(s => s.IsAvailable)
            .Take(5)
            .Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}")
            .ToList();

        if (topSlots.Count == 0)
        {
            return $"No available slots found for {match.Name} on {date:yyyy-MM-dd}.";
        }

        return $"Available slots for {match.Name} on {date:yyyy-MM-dd}: {string.Join(", ", topSlots)}";
    }

    private string ExecuteCreateBooking(JsonElement args)
    {
        var serviceName = GetStringArg(args, "serviceName");
        var date = GetStringArg(args, "date");
        var startTime = GetStringArg(args, "startTime");
        var customerName = GetStringArg(args, "customerName");
        var phone = GetStringArg(args, "phone");

        if (string.IsNullOrWhiteSpace(serviceName) ||
            string.IsNullOrWhiteSpace(date) ||
            string.IsNullOrWhiteSpace(startTime) ||
            string.IsNullOrWhiteSpace(customerName) ||
            string.IsNullOrWhiteSpace(phone))
        {
            return "To create a booking, please provide serviceName, date, startTime, customerName, and phone.";
        }

        return "Booking creation from chat is the next step. For now, please use the Booking page to complete your reservation.";
    }

    private static string? GetStringArg(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string ToIntent(string functionName) => functionName switch
    {
        "GetServicePrice" => "price",
        "SearchFaq" => "faq",
        "CheckAvailability" => "availability",
        "CreateBooking" => "booking",
        _ => "general"
    };

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
