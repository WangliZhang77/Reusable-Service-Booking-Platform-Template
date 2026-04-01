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
            return Task.FromResult("Which service should I look up? For example **Full Groom** or **Bath & Tidy**, or say **menu** for everything we offer.");
        }

        return GetServicePriceCoreAsync(serviceName, cancellationToken);
    }

    public Task<string> SearchFaqAsync(string? question, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Task.FromResult("What would you like to know? A short question or keyword works — for example cancellations, late arrival, or vaccines.");
        }

        return SearchFaqCoreAsync(question, cancellationToken);
    }

    public Task<string> CheckAvailabilityAsync(string? serviceName, string? dateText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(dateText))
        {
            return Task.FromResult("I will need the service (e.g. Full Groom) and a date in YYYY-MM-DD so I can check the diary.");
        }

        if (!DateOnly.TryParse(dateText, out var date))
        {
            return Task.FromResult("That date format did not quite parse — please use YYYY-MM-DD (for example 2026-04-15).");
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
            string.IsNullOrWhiteSpace(phone) ||
            string.IsNullOrWhiteSpace(petName))
        {
            return Task.FromResult(
                "I am missing a few must-haves to create the booking: service, date (YYYY-MM-DD), phone, and pet name. Send them together when you can.");
        }

        if (!DateOnly.TryParse(dateText, out var bookingDate))
        {
            return Task.FromResult("The booking date needs to be YYYY-MM-DD so our system accepts it.");
        }

        return CreateBookingCoreAsync(
            serviceName,
            bookingDate,
            startTimeText,
            customerName?.Trim(),
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
            _ => "That action is not available in chat yet — try asking for prices, availability, booking, or an FAQ topic."
        };
    }

    private async Task<string> GetServicePriceCoreAsync(string serviceName, CancellationToken cancellationToken)
    {
        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service on our menu. We offer **Full Groom** and **Bath & Tidy** — say **services** for the full list with prices.";
        }

        return $"{match.Name} is **NZD {match.Price:0.00}** and usually runs about **{match.DurationMinutes} minutes**. Want me to check open times on a date you have in mind?";
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
            return "I could not find a close FAQ match for that — try a shorter keyword (cancel, late, vaccine, first visit) or ask in different words and I will search again.";
        }

        return string.Join("\n\n", matched.Select(x => $"Q: {x.Question}\nA: {x.Answer}"));
    }

    private async Task<string> CheckAvailabilityCoreAsync(string serviceName, DateOnly date, CancellationToken cancellationToken)
    {
        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service name. Try **Full Groom** or **Bath & Tidy**, or type **menu** to see options.";
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
                return $"That day looks fully booked for {match.Name} ({date:yyyy-MM-dd}). I can try another date if you tell me what works for you.";
            }

            return $"Here is what is open for **{match.Name}** on **{date:yyyy-MM-dd}**: {string.Join(", ", topSlots)}. Tell me a start time you like and we can move to booking details.";
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
        string? customerName,
        string phone,
        string petName,
        string? petType,
        CancellationToken cancellationToken)
    {
        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var match = services.FirstOrDefault(s => s.Name.Contains(serviceName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return "I could not find that service — please pick **Full Groom** or **Bath & Tidy** (or say **menu**).";
        }

        TimeOnly startTime;
        if (!string.IsNullOrWhiteSpace(startTimeText))
        {
            if (!TryParseTime(startTimeText, out startTime))
            {
                return "That time format was tricky — please use HH:mm in 24-hour form (for example 09:30 or 14:00).";
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
                FullName = customerName,
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
            return $"All set — your booking is confirmed. Reference **{created.Id}**: **{created.ServiceName}** on **{created.BookingDate:yyyy-MM-dd}** at **{created.StartTime:HH:mm}** for **{petName}**. We will see you then!";
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
