using System.Text.Json;
using System.Text.Json.Serialization;
using BookingTemplate.Application.DTOs.Chat;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;

namespace BookingTemplate.Infrastructure.Services;

/// <summary>
/// 使用 Gemini Structured Output（JSON Schema）抽取意图与字段，供 C# 决定是否调用工具。
/// </summary>
public sealed class GeminiBookingIntentExtractor(IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<BookingIntentExtractionDto?> TryExtractAsync(string userMessage, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = configuration["Gemini:Model"] ?? "gemini-2.5-flash";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tomorrow = today.AddDays(1);

        var systemText =
            "You extract structured booking intent for a pet grooming salon. " +
            $"Today (UTC) is {today:yyyy-MM-dd}. Tomorrow is {tomorrow:yyyy-MM-dd}. " +
            "Map relative dates (today, tomorrow) to YYYY-MM-DD. " +
            "Map times like 12pm to 12:00 in 24h format. " +
            "intent must be one of: booking, availability, price, faq, general. " +
            "For faq intent, put the user's question keywords into faqQuery. " +
            "Do not invent names, phone numbers, or services not mentioned.";

        var responseSchema = """
        {
          "type": "object",
          "properties": {
            "intent": {
              "type": "string",
              "enum": ["booking", "availability", "price", "faq", "general"]
            },
            "serviceName": { "type": "string" },
            "date": { "type": "string", "description": "YYYY-MM-DD" },
            "startTime": { "type": "string", "description": "HH:mm 24h" },
            "customerName": { "type": "string" },
            "phone": { "type": "string" },
            "petName": { "type": "string" },
            "petType": { "type": "string" },
            "faqQuery": { "type": "string" },
            "missingFields": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": ["intent"],
          "propertyOrdering": ["intent","serviceName","date","startTime","customerName","phone","petName","petType","faqQuery","missingFields"]
        }
        """;

        try
        {
            var client = new Client(apiKey: apiKey);
            var config = new GenerateContentConfig
            {
                SystemInstruction = new Content { Parts = [Part.FromText(systemText)] },
                ResponseMimeType = "application/json",
                ResponseJsonSchema = JsonSerializer.Deserialize<object>(responseSchema)
            };

            var response = await client.Models.GenerateContentAsync(
                model: model,
                contents: userMessage,
                config: config,
                cancellationToken: cancellationToken);

            var text = response.Candidates?.FirstOrDefault()?.Content?.Parts?
                .Select(p => p.Text)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return JsonSerializer.Deserialize<BookingIntentExtractionDto>(text, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveApiKey()
    {
        var fromConfig = configuration["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig.Trim();
        }

        return System.Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    }
}
