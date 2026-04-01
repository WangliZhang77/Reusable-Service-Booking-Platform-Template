using System.Text.RegularExpressions;

namespace BookingTemplate.Application.Services;

/// <summary>
/// Strips chat/LLM artifacts mistaken for a person's name before persisting or showing in admin.
/// </summary>
public static class CustomerNameNormalizer
{
    private static readonly Regex TimeLike = new(
        @"^\s*\d{1,2}:\d{2}\s*$",
        RegexOptions.Compiled);

    private static readonly Regex DigitsOnly = new(
        @"^\s*\d+\s*$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> PlaceholderTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "am", "pm", "at", "on", "for", "to", "the", "me", "my", "i", "we", "you", "your",
        "hi", "hello", "hey", "good", "yes", "no", "ok", "okay", "please", "thanks", "thank",
        "have", "has", "had", "want", "need", "can", "could", "would", "will", "book", "booking",
        "afternoon", "morning", "evening", "night", "noon", "midday", "early", "earlier", "late", "later",
        "today", "tomorrow", "yesterday",
        "full", "groom", "bath", "tidy", "wash", "service", "available", "slot", "time",
        "dog", "cat", "pet", "name", "call", "phone", "number",
    };

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length < 2)
        {
            return null;
        }

        if (TimeLike.IsMatch(trimmed) || DigitsOnly.IsMatch(trimmed))
        {
            return null;
        }

        var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        if (tokens.Length == 1 && PlaceholderTokens.Contains(StripEdges(trimmed)))
        {
            return null;
        }

        if (tokens.All(t => PlaceholderTokens.Contains(StripEdges(t))))
        {
            return null;
        }

        return trimmed;
    }

    private static string StripEdges(string token)
    {
        return token.Trim().TrimEnd('.', ',', '!', '?', ':', ';');
    }
}
