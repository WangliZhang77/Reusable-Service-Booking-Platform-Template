using System.Text.Json;
using BookingTemplate.Application.DTOs.Bookings;
using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Application.Interfaces.Services;
using Google.GenAI.Types;

namespace BookingTemplate.Infrastructure.Services;

/// <summary>
/// 聊天工具的真实后端执行（可被单元测试与 Gemini 两条路径调用）。
/// </summary>
public sealed class BookingChatToolExecutor(IBookingDataAccess dataAccess, IBookingService bookingService)
{
    public Task<string> GetServicePriceAsync(string? serviceName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return Task.FromResult("Please provide a service name to check pricing.");
        }

        return GetServicePriceCoreAsync(serviceName, cancellationToken);
    }

    public Task<string> SearchFaqAsync(string? question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Task.FromResult("Please tell me what you want to know.");
        }

        return SearchFaqCoreAsync(question, cancellationToken);
    }

    public Task<string> CheckAvailabilityAsync(string? serviceName, string? dateText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(dateText))
        {
            return Task.FromResult("Please provide both service name and date (YYYY-MM-DD).");
        }

        if (!DateOnly.TryParse(dateText, out var date))
        {
            return Task.FromResult("Date format is invalid. Use YYYY-MM-DD.");
        }

        return CheckAvailabilityCoreAsync(serviceName, date, cancellationToken);
    }

    public Task<string> CreateBookingAsync(
        string? serviceName,
        string? dateText,
        string? startTimeText,
        string? customerName,
        string? phone,
        string? petName,
        string? petType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName) ||
            string.IsNullOrWhiteSpace(dateText) ||
            string.IsNullOrWhiteSpace(customerName) ||
            string.IsNullOrWhiteSpace(phone) ||
            string.IsNullOrWhiteSpace(petName))
        {
            return Task.FromResult(
                "Missing required fields. Need serviceName, date (YYYY-MM-DD), customerName, phone, and petName.");
        }

        if (!DateOnly.TryParse(dateText, out var bookingDate))
        {
            return Task.FromResult("Invalid date. Use YYYY-MM-DD.");
        }

        return CreateBookingCoreAsync(
            serviceName,
            bookingDate,
            startTimeText,
            customerName,
            phone,
            petName,
            petType,
            cancellationToken);
    }

    public async Task<string> ExecuteFunctionCallAsync(FunctionCall fc, CancellationToken cancellationToken)
    {
        var args = fc.Args ?? new Dictionary<string, object>();
        var name = fc.Name ?? string.Empty;

        return name switch
        {
            "GetServicePrice" => await GetServicePriceAsync(GetStringArg(args, "serviceName"), cancellationToken),
            "SearchFaq" => await SearchFaqAsync(GetStringArg(args, "question"), cancellationToken),
            "CheckAvailability" => await CheckAvailabilityAsync(
                GetStringArg(args, "serviceName"),
                GetStringArg(args, "date"),
                cancellationToken),
            "CreateBooking" => await CreateBookingAsync(
                GetStringArg(args, "serviceName"),
                GetStringArg(args, "date"),
                GetStringArg(args, "startTime"),
                GetStringArg(args, "customerName"),
                GetStringArg(args, "phone"),
                GetStringArg(args, "petName"),
                GetStringArg(args, "petType"),
                cancellationToken),
            _ => "Unknown tool."
        };
    }

    private async Task<string> GetServicePriceCoreAsync(string serviceName, CancellationToken cancellationToken)
    {
        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service. Please share the exact service name.";
        }

        return $"{match.Name} costs NZD {match.Price:0.00} and takes about {match.DurationMinutes} minutes.";
    }

    private async Task<string> SearchFaqCoreAsync(string query, CancellationToken cancellationToken)
    {
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

    private async Task<string> CheckAvailabilityCoreAsync(string serviceName, DateOnly date, CancellationToken cancellationToken)
    {
        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service. Please share the exact service name.";
        }

        try
        {
            var availability = await bookingService.GetAvailabilityAsync(match.Id, date, cancellationToken);

            var topSlots = availability.Slots
                .Where(s => s.IsAvailable)
                .Take(8)
                .Select(s => $"{s.StartTime:HH:mm}-{s.EndTime:HH:mm}")
                .ToList();

            if (topSlots.Count == 0)
            {
                return $"No available slots found for {match.Name} on {date:yyyy-MM-dd}.";
            }

            return $"Available slots for {match.Name} on {date:yyyy-MM-dd}: {string.Join(", ", topSlots)}";
        }
        catch (Exception ex)
        {
            return $"Could not check availability: {ex.Message}";
        }
    }

    private async Task<string> CreateBookingCoreAsync(
        string serviceName,
        DateOnly bookingDate,
        string? startTimeText,
        string customerName,
        string phone,
        string petName,
        string? petType,
        CancellationToken cancellationToken)
    {
        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service. Please confirm the service name.";
        }

        TimeOnly startTime;
        if (!string.IsNullOrWhiteSpace(startTimeText))
        {
            if (!TryParseTime(startTimeText, out startTime))
            {
                return "Invalid start time. Use HH:mm (24-hour), e.g. 12:00.";
            }
        }
        else
        {
            var availability = await bookingService.GetAvailabilityAsync(match.Id, bookingDate, cancellationToken);
            var first = availability.Slots.FirstOrDefault(s => s.IsAvailable);
            if (first is null)
            {
                return $"No available slots found for {match.Name} on {bookingDate:yyyy-MM-dd}.";
            }

            startTime = first.StartTime;
        }

        var dto = new CreateBookingRequestDto
        {
            ServiceId = match.Id,
            BookingDate = bookingDate,
            StartTime = startTime,
            Customer = new CustomerInputDto
            {
                FullName = customerName.Trim(),
                Phone = phone.Trim()
            },
            Pet = new PetInputDto
            {
                Name = petName.Trim(),
                Species = NormalizeSpecies(petType)
            }
        };

        try
        {
            var created = await bookingService.CreateBookingAsync(dto, cancellationToken);
            return $"Done - your booking is confirmed. Ref: {created.Id}. {created.ServiceName} on {created.BookingDate:yyyy-MM-dd} at {created.StartTime:HH:mm} for {petName}.";
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        catch (KeyNotFoundException ex)
        {
            return ex.Message;
        }
    }

    private static bool TryParseTime(string text, out TimeOnly time)
    {
        text = text.Trim();
        if (TimeOnly.TryParse(text, out time))
        {
            return true;
        }

        if (TimeSpan.TryParse(text, out var ts))
        {
            time = TimeOnly.FromTimeSpan(ts);
            return true;
        }

        time = default;
        return false;
    }

    private static string NormalizeSpecies(string? petType)
    {
        var t = (petType ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return "Unknown";
        }

        var lower = t.ToLowerInvariant();
        if (lower.Contains("狗") || lower.Contains("dog") || lower.Contains("犬"))
        {
            return "Dog";
        }

        if (lower.Contains("猫") || lower.Contains("cat"))
        {
            return "Cat";
        }

        if (t.Length == 1)
        {
            return t.ToUpperInvariant();
        }

        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static string? GetStringArg(IReadOnlyDictionary<string, object> args, string name)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.GetRawText()
            };
        }

        return value.ToString();
    }
}
