using System.Text.Json;
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

    public async Task<ChatResponseDto?> ReplyWithGeminiAndToolsAsync(string message, CancellationToken cancellationToken)
    {
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
                    return new ChatResponseDto(FormatFollowUp(missing), intentNorm);
                }

                if (intentNorm == "availability" && missing.Count == 0)
                {
                    var slotText = await toolExecutor.CheckAvailabilityAsync(
                        extracted.ServiceName,
                        extracted.Date,
                        cancellationToken);
                    return new ChatResponseDto(slotText, "availability");
                }

                if (intentNorm == "booking" && missing.Count == 0)
                {
                    var bookText = await toolExecutor.CreateBookingAsync(
                        extracted.ServiceName,
                        extracted.Date,
                        extracted.StartTime,
                        extracted.CustomerName,
                        extracted.Phone,
                        extracted.PetName,
                        extracted.PetType,
                        cancellationToken);
                    return new ChatResponseDto(bookText, "booking");
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
                        "You are a helpful assistant for a pet grooming / local service booking website. " +
                        "Use the provided tools when the user asks about pricing, FAQ, availability, or wants to book. " +
                        "If required parameters are missing, ask a short follow-up question instead of guessing. " +
                        "For dates and times, output tool arguments as YYYY-MM-DD and HH:mm (24h). " +
                        "Prefer calling CheckAvailability and CreateBooking when relevant.")
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

    private static string FormatFollowUp(IReadOnlyList<string> missing)
    {
        static string Label(string key) => key switch
        {
            "serviceName" => "service name (服务名称)",
            "date" => "date YYYY-MM-DD (日期)",
            "startTime" => "start time HH:mm (开始时间)",
            "customerName" => "your name (姓名)",
            "phone" => "phone (电话)",
            "petName" => "pet name (宠物名)",
            "petType" => "pet type e.g. dog/cat (宠物类型)",
            "faqQuery" => "what you want to ask (具体问题)",
            _ => key
        };

        var parts = missing.Select(Label).ToList();
        return "I still need a bit more information:\n• " +
               string.Join("\n• ", parts) +
               "\nPlease reply with these details.";
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
                    Description = "Create a booking after you have service name, date, start time, customer name, phone, pet name, and pet type/species.",
                    ParametersJsonSchema = ParseSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "serviceName": { "type": "string" },
                        "date": { "type": "string", "description": "YYYY-MM-DD" },
                        "startTime": { "type": "string", "description": "HH:mm 24h" },
                        "customerName": { "type": "string" },
                        "phone": { "type": "string" },
                        "petName": { "type": "string" },
                        "petType": { "type": "string", "description": "Species e.g. dog, cat" }
                      },
                      "required": ["serviceName", "date", "startTime", "customerName", "phone", "petName", "petType"]
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
}
