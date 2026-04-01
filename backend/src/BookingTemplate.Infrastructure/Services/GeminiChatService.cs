using System.Text.Json;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using BookingTemplate.Application.DTOs.Chat;
using BookingTemplate.Application.Interfaces.Services;
using BookingTemplate.Application.Services;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Configuration;

namespace BookingTemplate.Infrastructure.Services;

public sealed class GeminiChatService(
    BookingChatToolExecutor toolExecutor,
    GeminiBookingIntentExtractor intentExtractor,
    IConfiguration configuration) : IGeminiChatService
{
    private const int MaxToolRounds = 8;
    private static readonly ConcurrentDictionary<string, AvailabilityContext> AvailabilityStates = new(StringComparer.Ordinal);
    private static readonly Regex SlotInputRegex = new(@"^\s*(\d{1,2}:\d{2})(\s*-\s*\d{1,2}:\d{2})?\s*,?\s*$", RegexOptions.Compiled);
    private static readonly Regex BookingConfirmationRegex = new(
        @"yes, confirm\s+([A-Za-z0-9+/=]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<ChatResponseDto?> ReplyWithGeminiAndToolsAsync(string message, string? sessionId, CancellationToken cancellationToken)
    {
        var sessionKey = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId.Trim();

        if (TryParseConfirmation(message, out var pending))
        {
            AvailabilityStates.TryRemove(sessionKey, out _);
            var bookText = await toolExecutor.CreateBookingAsync(
                pending.ServiceName,
                pending.Date,
                pending.StartTime,
                pending.CustomerName,
                pending.Phone,
                pending.PetName,
                pending.PetType,
                cancellationToken);
            return new ChatResponseDto(bookText, "booking");
        }

        if (TryHandleSlotFollowUp(message, sessionKey, out var followUpReply))
        {
            return followUpReply;
        }

        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = configuration["Gemini:Model"] ?? "gemini-2.5-flash";

        try
        {
            var extracted = await intentExtractor.TryExtractAsync(message, cancellationToken);
            if (extracted is not null)
            {
                if (string.Equals(extracted.Intent, "faq", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(extracted.FaqQuery))
                {
                    extracted.FaqQuery = message;
                }

                var missing = BookingIntentMissingFields.Compute(extracted);
                var intentNorm = (extracted.Intent ?? "general").Trim().ToLowerInvariant();

                if (missing.Count > 0 && intentNorm is "booking" or "availability" or "price" or "faq")
                {
                    return new ChatResponseDto(FormatFollowUp(intentNorm, missing), intentNorm);
                }

                if (intentNorm == "availability" && missing.Count == 0)
                {
                    var slotText = await toolExecutor.CheckAvailabilityAsync(
                        extracted.ServiceName,
                        extracted.Date,
                        cancellationToken);
                    AvailabilityStates[sessionKey] = new AvailabilityContext(extracted.ServiceName!, extracted.Date!);
                    return new ChatResponseDto(slotText, "availability");
                }

                if (intentNorm == "booking" && missing.Count == 0)
                {
                    return BookingConfirmationFormatter.ToResponse(new BookingConfirmationPayload(
                        extracted.ServiceName!,
                        extracted.Date!,
                        extracted.StartTime,
                        CustomerNameNormalizer.Normalize(extracted.CustomerName),
                        extracted.Phone!,
                        extracted.PetName!,
                        extracted.PetType));
                }

                if (intentNorm == "price" && missing.Count == 0)
                {
                    var priceText = await toolExecutor.GetServicePriceAsync(extracted.ServiceName, cancellationToken);
                    return new ChatResponseDto(priceText, "price");
                }

                if (intentNorm == "faq" && missing.Count == 0)
                {
                    var faqText = await toolExecutor.SearchFaqAsync(extracted.FaqQuery, cancellationToken);
                    return new ChatResponseDto(faqText, "faq");
                }
            }

            return await RunFunctionCallingLoopAsync(model, message, apiKey, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> ReplyWithGeminiTextAsync(string systemInstruction, string userMessage, CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = configuration["Gemini:Model"] ?? "gemini-2.5-flash";
        try
        {
            var client = new Client(apiKey: apiKey);
            var config = new GenerateContentConfig
            {
                SystemInstruction = new Content
                {
                    Parts = [Part.FromText(systemInstruction)]
                }
            };

            var response = await client.Models.GenerateContentAsync(
                model: model,
                contents: userMessage,
                config: config,
                cancellationToken: cancellationToken);

            var text = response.Candidates?.FirstOrDefault()?.Content?.Parts?
                .Select(p => p.Text)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<ChatResponseDto?> RunFunctionCallingLoopAsync(
        string model,
        string message,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = new Client(apiKey: apiKey);
            var tools = new List<Tool> { BuildBookingTools() };

            var systemInstruction = new Content
            {
                Parts =
                [
                    Part.FromText(
                        "You are the warm, professional front-desk assistant for a pet grooming salon website. " +
                        "Sound human: acknowledge feelings (especially nervous pets or first-time visits), use short paragraphs, and avoid robotic lists unless listing times or prices. " +
                        "Recognize intent: small talk → brief friendly reply then gently offer menu / booking / FAQ; pricing → GetServicePrice; open slots → CheckAvailability; ready to commit with full details → CreateBooking; general questions → SearchFaq. " +
                        "Never invent services, prices, or policies — use tools for facts. " +
                        "If parameters are missing, ask one clear follow-up at a time instead of guessing. " +
                        "Dates and times in tool calls must be YYYY-MM-DD and HH:mm (24-hour). " +
                        "Prefer tools over long speculation; after tool results, summarize in natural language.")
                ]
            };

            var config = new GenerateContentConfig
            {
                SystemInstruction = systemInstruction,
                Tools = tools
            };

            var contents = new List<Content>
            {
                new Content { Role = "user", Parts = [Part.FromText(message)] }
            };

            string? lastIntent = null;

            for (var round = 0; round < MaxToolRounds; round++)
            {
                var response = await client.Models.GenerateContentAsync(
                    model: model,
                    contents: contents,
                    config: config,
                    cancellationToken: cancellationToken);

                var candidate = response.Candidates?.FirstOrDefault();
                if (candidate?.Content?.Parts is null || candidate.Content.Parts.Count == 0)
                {
                    return null;
                }

                var modelContent = new Content
                {
                    Role = "model",
                    Parts = candidate.Content.Parts
                };

                var functionCalls = candidate.Content.Parts
                    .Where(p => p.FunctionCall is not null)
                    .Select(p => p.FunctionCall!)
                    .ToList();

                if (functionCalls.Count == 0)
                {
                    var text = candidate.Content.Parts
                        .Select(p => p.Text)
                        .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new ChatResponseDto(text.Trim(), lastIntent ?? "general");
                    }

                    return null;
                }

                contents.Add(modelContent);

                var responseParts = new List<Part>();
                foreach (var fc in functionCalls)
                {
                    lastIntent = MapToolToIntent(fc.Name);
                    var toolText = await toolExecutor.ExecuteFunctionCallAsync(fc, cancellationToken);
                    var dict = new Dictionary<string, object> { ["output"] = toolText };
                    if (!string.IsNullOrEmpty(fc.Id))
                    {
                        responseParts.Add(new Part
                        {
                            FunctionResponse = new FunctionResponse
                            {
                                Id = fc.Id,
                                Name = fc.Name,
                                Response = dict
                            }
                        });
                    }
                    else
                    {
                        responseParts.Add(Part.FromFunctionResponse(fc.Name ?? "unknown", dict));
                    }
                }

                contents.Add(new Content { Role = "user", Parts = responseParts });
            }

            return new ChatResponseDto(
                "I could not finish that request within the tool round limit. Please simplify or try again.",
                lastIntent ?? "general");
        }
        catch
        {
            return null;
        }
    }

    private static string FormatFollowUp(string intent, IReadOnlyList<string> missing)
    {
        static int SortKey(string key) => key switch
        {
            "serviceName" => 1,
            "date" => 2,
            "startTime" => 3,
            "customerName" => 4,
            "phone" => 5,
            "petName" => 6,
            "petType" => 7,
            "faqQuery" => 8,
            _ => 100
        };

        static string Label(string key) => key switch
        {
            "serviceName" => "service name",
            "date" => "date (YYYY-MM-DD)",
            "startTime" => "start time (HH:mm, 24-hour)",
            "customerName" => "your name",
            "phone" => "phone number",
            "petName" => "pet name",
            "petType" => "pet type (e.g. dog/cat)",
            "faqQuery" => "what you want to ask",
            _ => key
        };

        var sorted = missing.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(SortKey)
            .ToList();
        var parts = sorted.Select(Label).ToList();
        var example = intent switch
        {
            "booking" => "Example: Full Groom, 2026-04-01, 0211234567, Coco (name optional if unsure)",
            "availability" => "Example: Full Groom, 2026-04-01",
            "price" => "Example: Full Groom",
            "faq" => "Example: Do you groom senior cats?",
            _ => string.Empty
        };

        return "Happy to help — I just need a bit more so I get it right for you:\n• " +
               string.Join("\n• ", parts) +
               "\nYou can send everything in one message if that is easier." +
               (string.IsNullOrWhiteSpace(example) ? string.Empty : $"\n{example}");
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

    private static string MapToolToIntent(string? name) => name switch
    {
        "GetServicePrice" => "price",
        "SearchFaq" => "faq",
        "CheckAvailability" => "availability",
        "CreateBooking" => "booking",
        _ => "general"
    };

    private static Tool BuildBookingTools()
    {
        return new Tool
        {
            FunctionDeclarations =
            [
                new FunctionDeclaration
                {
                    Name = "CheckAvailability",
                    Description = "Check available booking slots for a service on a given date.",
                    ParametersJsonSchema = ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "serviceName": { "type": "string", "description": "Service name as the user said or from catalog" },
                        "date": { "type": "string", "description": "Date in YYYY-MM-DD" }
                      },
                      "required": ["serviceName", "date"]
                    }
                    """)
                },
                new FunctionDeclaration
                {
                    Name = "CreateBooking",
                    Description = "Create a booking when you have service name, date, start time, phone, pet name, and pet type. Customer name is optional — omit or leave empty if unknown; never use time-of-day words as a name.",
                    ParametersJsonSchema = ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "serviceName": { "type": "string" },
                        "date": { "type": "string", "description": "YYYY-MM-DD" },
                        "startTime": { "type": "string", "description": "HH:mm 24h" },
                        "customerName": { "type": "string", "description": "Optional. Real person name only; omit if not provided." },
                        "phone": { "type": "string" },
                        "petName": { "type": "string" },
                        "petType": { "type": "string", "description": "Species e.g. dog, cat" }
                      },
                      "required": ["serviceName", "date", "startTime", "phone", "petName", "petType"]
                    }
                    """)
                },
                new FunctionDeclaration
                {
                    Name = "GetServicePrice",
                    Description = "Get price and duration for a service by name.",
                    ParametersJsonSchema = ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "serviceName": { "type": "string" }
                      },
                      "required": ["serviceName"]
                    }
                    """)
                },
                new FunctionDeclaration
                {
                    Name = "SearchFaq",
                    Description = "Search published FAQ entries by user question or keywords.",
                    ParametersJsonSchema = ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "question": { "type": "string", "description": "Search query" }
                      },
                      "required": ["question"]
                    }
                    """)
                }
            ]
        };
    }

    private static object ParseSchema(string json) =>
        JsonSerializer.Deserialize<object>(json) ?? new { };

    private static bool TryParseConfirmation(string message, out BookingConfirmationPayload pending)
    {
        pending = default!;
        var match = BookingConfirmationRegex.Match(message.Trim());
        if (!match.Success)
        {
            return false;
        }

        var token = match.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var json = Convert.FromBase64String(token);
            var parsed = JsonSerializer.Deserialize<BookingConfirmationPayload>(json);
            if (parsed is null ||
                string.IsNullOrWhiteSpace(parsed.ServiceName) ||
                string.IsNullOrWhiteSpace(parsed.Date) ||
                string.IsNullOrWhiteSpace(parsed.Phone) ||
                string.IsNullOrWhiteSpace(parsed.PetName))
            {
                return false;
            }

            pending = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryHandleSlotFollowUp(string message, string sessionKey, out ChatResponseDto reply)
    {
        reply = default!;
        var match = SlotInputRegex.Match(message);
        if (!match.Success)
        {
            return false;
        }

        if (!AvailabilityStates.TryGetValue(sessionKey, out var state))
        {
            reply = new ChatResponseDto("What service and date are you interested in for this slot?", "availability");
            return true;
        }

        var start = match.Groups[1].Value;
        reply = new ChatResponseDto(
            $"Great, I can book {state.ServiceName} on {state.Date} at {start}. " +
            "Please send your details in one message: your name, phone number, pet name, and pet type (dog/cat).",
            "booking");
        return true;
    }

    private sealed record AvailabilityContext(string ServiceName, string Date);
}
