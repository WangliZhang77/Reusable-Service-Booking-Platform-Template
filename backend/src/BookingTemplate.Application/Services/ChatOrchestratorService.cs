using BookingTemplate.Application.DTOs.Chat;
using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Application.Interfaces.Services;
using BookingTemplate.Domain.Entities;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace BookingTemplate.Application.Services;

/// <summary>
/// 聊天编排：先本地 FAQ / 服务与价格，再交给 Gemini + 工具。
/// </summary>
public sealed class ChatOrchestratorService(
    IBookingDataAccess dataAccess,
    IGeminiChatService geminiChat,
    IBookingService bookingService) : IChatService
{
    private enum ConversationStage
    {
        browsing,
        asking_about_service,
        comparing_services,
        ready_to_book,
        booking_in_progress
    }

    private static readonly ConcurrentDictionary<string, BookingDraft> BookingDrafts = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ConversationMemory> Memories = new(StringComparer.Ordinal);
    private static readonly Regex DateRegex = new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);
    /// <summary>2026/4/2, 2026-04-2, 2026.04.02</summary>
    private static readonly Regex SlashOrDotDateRegex = new(@"\b(\d{4})[/.-](\d{1,2})[/.-](\d{1,2})\b", RegexOptions.Compiled);
    /// <summary>26/04/02 → 2026-04-02（两位年份补 2000+）。</summary>
    private static readonly Regex TwoDigitYearDateRegex = new(@"\b(\d{2})[/.-](\d{1,2})[/.-](\d{1,2})\b", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"\b([01]?\d|2[0-3])(?::([0-5]\d))?\b", RegexOptions.Compiled);
    private static readonly Regex NameRegex = new(@"(?:my\s*name|myname)\s+is\s+([a-z][a-z\s'\-]{1,40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LooseNameRegex = new(@"(?:name is|i am|this is)\s+([a-z][a-z\s'\-]{1,40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"\b(?:\+?\d[\d\-\s]{6,16}\d)\b", RegexOptions.Compiled);
    private static readonly Regex PetNameRegex = new(@"(?:pet name is|pet is|dog is|cat is)\s+([a-z0-9][a-z0-9\s'\-]{0,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DogCatNameRegex = new(@"(?:dog|cat)\s+name\s+([a-z0-9][a-z0-9\s'\-]{0,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Matches "pet Fluffy" but not "pet name is …" (that is handled by <see cref="PetNameRegex"/>).</summary>
    private static readonly Regex PetPlainNameRegex = new(@"pet\s+(?!name\b)([a-z0-9][a-z0-9\s'\-]{0,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PetCalledNameRegex = new(@"(?:pet|dog|cat)\s+(?:is\s+)?called\s+([a-z0-9][a-z0-9\s'\-]{0,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SimpleNameRegex = new(@"^[a-z][a-z\s'\-]{1,40}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyClockRegex = new(@"\b\d{1,2}:\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex BookingConfirmationRegex = new(
        @"yes, confirm\s+([A-Za-z0-9+/=]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> FaqWeakTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "dog", "dogs", "cat", "cats", "pet", "pets"
    };

    private static readonly HashSet<string> BookingInlineNameNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "book", "booking", "reserve", "appointment", "full", "groom", "grooming", "bath", "tidy", "dog", "dogs", "cat", "cats",
        "pet", "pets", "my", "the", "for", "and", "with", "want", "wanna", "need", "please", "tomorrow", "today", "yes", "no", "ok"
    };

    private static readonly Dictionary<string, string> ServiceAudienceHints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Full Groom"] = "Best for pets that need a full styling session, haircut, and complete grooming care.",
        ["Bath & Tidy"] = "Best for quick refresh visits, gentle maintenance, and first-time or nervous pets."
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "do", "does", "did", "can", "could", "you", "your",
        "i", "we", "to", "for", "of", "in", "on", "at", "and", "or", "with", "what", "how",
        "when", "where", "why", "our", "my", "me", "it"
    };

    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cat"] = ["cats", "feline", "kitty"],
        ["cats"] = ["cat", "feline", "kitty"],
        ["dog"] = ["dogs", "canine", "puppy"],
        ["dogs"] = ["dog", "canine", "puppy"],
        ["groom"] = ["grooming", "trim", "wash", "bath"],
        ["grooming"] = ["groom", "trim", "wash", "bath"],
        ["open"] = ["opening", "hours", "hour", "business"],
        ["cancel"] = ["cancellation", "reschedule", "refund"],
        ["late"] = ["delay", "delayed", "迟到"]
    };

    public async Task<ChatResponseDto> ReplyAsync(ChatRequestDto request, CancellationToken cancellationToken)
    {
        var message = request.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message is required.");
        }
        var intent = DetectIntent(message);
        var normalized = message.ToLowerInvariant();
        var sessionId = request.SessionId;
        var sessionKey = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId.Trim();
        var memory = Memories.GetOrAdd(sessionKey, _ => new ConversationMemory());
        var hasDraft = BookingDrafts.TryGetValue(sessionKey, out var draft);

        var conversational = TryReplyConversationalCue(normalized, intent);
        if (conversational is not null)
        {
            memory.LastIntent = conversational.Intent;
            return conversational;
        }
        if (message.All(ch => !char.IsLetterOrDigit(ch)))
        {
            return new ChatResponseDto(
                "I did not catch that message clearly. Ask me about services, pricing, availability, or booking and I will help right away.",
                "fallback");
        }

        var stage = DetectStage(normalized, intent, hasDraft && string.Equals(draft?.Mode, "booking", StringComparison.OrdinalIgnoreCase));

        if (normalized.Contains("how does booking work") || normalized.Contains("what do i need to prepare"))
        {
            return new ChatResponseDto(
                "Booking is straightforward: choose a service and date, then share your name, phone, and pet name. I always show you a summary first — nothing is final until you confirm, so you can fix details anytime before that.",
                "faq");
        }

        if (normalized.Contains("what can you do") || normalized.Contains("what do you do") ||
            normalized.Contains("how does this work") || normalized.Contains("how do i use this") ||
            normalized.Contains("i need help") || normalized.Contains("help me") ||
            normalized.Contains("where do i start") || normalized.Contains("not sure where to start") ||
            normalized.Contains("im lost") || normalized.Contains("i'm lost"))
        {
            return new ChatResponseDto(
                "Here is how I can help, in plain language:\n\n" +
                "• **Pick a service** — tell me about your pet and I will suggest Full Groom vs Bath & Tidy, or say **menu** for the full list\n" +
                "• **Prices & time** — ask \"how much is Full Groom?\" anytime\n" +
                "• **Availability** — share a service and a date (for example tomorrow or 2026-04-15)\n" +
                "• **Book** — once we have the basics, I will show a summary and you confirm in one tap\n\n" +
                "Nervous or first-time pet? Tell me — I will factor that into the recommendation.",
                "capabilities");
        }

        if ((normalized.Contains("not a cat") && normalized.Contains("dog")) || (normalized.Contains("not a dog") && normalized.Contains("cat")))
        {
            var petType = normalized.Contains("not a cat") ? "dog" : "cat";
            memory.LastUserProfile = (memory.LastUserProfile + " " + petType).Trim();
            return new ChatResponseDto(
                $"Got it - I updated your pet as {petType}. Want me to recommend the best service and check availability?",
                "asking_about_service");
        }

        // Date hesitation only — do not hijack "not sure if I should groom or wash".
        if (normalized.Contains("not sure") &&
            (normalized.Contains("tomorrow") || normalized.Contains("next week") || normalized.Contains("maybe") || normalized.Contains("今天") || normalized.Contains("明天")))
        {
            return new ChatResponseDto(
                "No rush at all. Tell me your pet type and whether you want a quick wash or full groom, and I can suggest the best option before booking.",
                "browsing");
        }

        if (normalized == "price?" || normalized == "price")
        {
            if (!string.IsNullOrWhiteSpace(memory.LastServiceName))
            {
                var lastServicePrice = await TryAnswerFromServiceAsync($"price {memory.LastServiceName}", "pricing", memory, cancellationToken);
                if (lastServicePrice is not null)
                {
                    return lastServicePrice;
                }
            }

            return new ChatResponseDto(
                "Sure - tell me which service you want pricing for (for example: Full Groom or Bath & Tidy).",
                "pricing");
        }

        if ((normalized.Contains("how much is it") || normalized == "how much") && !string.IsNullOrWhiteSpace(memory.LastServiceName))
        {
            var lastServicePrice = await TryAnswerFromServiceAsync($"price {memory.LastServiceName}", "pricing", memory, cancellationToken);
            if (lastServicePrice is not null)
            {
                return lastServicePrice;
            }
        }

        // Booking / availability before service "consultation" so phrases like "book Wash & Tidy" are not eaten by heuristics.
        if (intent is "booking" or "availability")
        {
            var priorityBooking = await TryHandleLocalBookingFlowAsync(message, normalized, intent, sessionKey, memory, cancellationToken);
            if (priorityBooking is not null)
            {
                memory.LastIntent = priorityBooking.Intent;
                return priorityBooking;
            }
        }

        // After we recommended a service, a short "yes" should start the booking flow.
        if (IsShortBookingAffirmation(normalized) && !string.IsNullOrWhiteSpace(memory.LastServiceName))
        {
            var affirmed = $"I want to book {memory.LastServiceName} for my pet";
            var affirmedNorm = affirmed.ToLowerInvariant();
            var affirmedReply = await TryHandleLocalBookingFlowAsync(affirmed, affirmedNorm, "booking", sessionKey, memory, cancellationToken);
            if (affirmedReply is not null)
            {
                memory.LastIntent = affirmedReply.Intent;
                return affirmedReply;
            }
        }

        if (stage is ConversationStage.asking_about_service or ConversationStage.comparing_services)
        {
            var advisory = await TryAnswerServiceAdvisoryAsync(message, normalized, stage, memory, cancellationToken);
            if (advisory is not null)
            {
                memory.LastIntent = advisory.Intent;
                return advisory;
            }
        }

        if ((normalized.Contains("can i do tomorrow") || normalized.Contains("book that tomorrow") || normalized.Contains("can i book that tomorrow")) &&
            !string.IsNullOrWhiteSpace(memory.LastServiceName))
        {
            var nextDate = ParseDate(normalized) ?? DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(1).ToString("yyyy-MM-dd");
            var quickAvailability = await TryBuildAvailabilityReplyAsync(memory.LastServiceName!, nextDate, normalized, cancellationToken);
            if (quickAvailability is not null)
            {
                return quickAvailability;
            }
        }

        if (normalized.Contains("can i do tomorrow") || normalized.Contains("book that tomorrow") || normalized.Contains("can i book that tomorrow"))
        {
            return new ChatResponseDto(
                "Sure, we can do that tomorrow. Tell me which service you want, and I will check available slots right away.",
                "availability");
        }

        // Service list must win over booking draft so users see options without losing the draft.
        if (AskingServiceMenu(normalized))
        {
            var menuEarly = await TryAnswerFromServiceAsync(message, intent, memory, cancellationToken);
            if (menuEarly is not null)
            {
                memory.LastIntent = menuEarly.Intent;
                return menuEarly;
            }
        }

        var localBooking = await TryHandleLocalBookingFlowAsync(message, normalized, intent, sessionKey, memory, cancellationToken);
        if (localBooking is not null)
        {
            memory.LastIntent = localBooking.Intent;
            return localBooking;
        }

        // Booking/availability and confirmation should bypass FAQ-first routing.
        if (intent is "booking" or "availability" || IsConfirmationMessage(normalized))
        {
            var prioritized = await geminiChat.ReplyWithGeminiAndToolsAsync(message, sessionId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(prioritized?.Reply))
            {
                if (IsConfirmationMessage(normalized))
                {
                    BookingDrafts.TryRemove(sessionKey, out _);
                }

                memory.LastIntent = prioritized!.Intent;
                return prioritized!;
            }
        }

        var fromService = await TryAnswerFromServiceAsync(message, intent, memory, cancellationToken);
        if (fromService is not null)
        {
            memory.LastIntent = fromService.Intent;
            return fromService;
        }

        var fromFaq = await TryAnswerFromFaqAsync(message, intent, cancellationToken);
        if (fromFaq is not null)
        {
            memory.LastIntent = fromFaq.Intent;
            return fromFaq;
        }

        var geminiResult = await geminiChat.ReplyWithGeminiAndToolsAsync(message, sessionId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(geminiResult?.Reply))
        {
            memory.LastIntent = geminiResult!.Intent;
            return geminiResult!;
        }

        return new ChatResponseDto(
            "I want to make sure you get the right help.\n\n" +
            "You can try:\n" +
            "• Say **services** or **menu** to see grooming options and prices\n" +
            "• Tell me your pet (dog/cat), preferred date, and that you want to **book** — I will walk you through it\n" +
            "• Ask a specific question (hours, cancellations, nervous pets, etc.) and I will match it to our FAQ\n\n" +
            "If something still does not feel right, our team is happy to help on the phone or email from the Contact page.",
            "fallback");
    }

    private async Task<ChatResponseDto?> TryAnswerFromFaqAsync(string message, string intent, CancellationToken cancellationToken)
    {
        if (intent is "booking" or "availability")
        {
            return null;
        }

        var loweredMessage = message.ToLowerInvariant();
        if (IsRecommendationQuestion(loweredMessage) || IsComparisonQuestion(loweredMessage) || IsServiceQuestion(loweredMessage) || IsGroomingNeedStatement(loweredMessage))
        {
            return null;
        }

        List<Domain.Entities.Faq> faqs;
        try
        {
            faqs = await dataAccess.GetPublishedFaqsAsync(cancellationToken);
        }
        catch
        {
            return null;
        }

        if (faqs.Count == 0)
        {
            return null;
        }

        var normalized = NormalizeText(message);
        var baseTerms = ExtractTerms(normalized);
        if (baseTerms.Count > 0 && baseTerms.All(FaqWeakTerms.Contains))
        {
            return null;
        }

        var messageTerms = ExpandTerms(baseTerms);

        var best = faqs
            .Select(f =>
            {
                var q = NormalizeText(f.Question);
                var a = NormalizeText(f.Answer);
                var score = ScoreFaq(normalized, messageTerms, q, a);
                return new { faq = f, score };
            })
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        // Threshold keeps weak/noisy matches from hijacking normal flow.
        if (best is null || best.score < 4)
        {
            return null;
        }

        var answer = best.score < 6
            ? "This might be what you are looking for — let me know if you need more detail:\n\n" + best.faq.Answer
            : best.faq.Answer;

        return new ChatResponseDto(answer, intent == "general" ? "faq" : intent);
    }

    private async Task<ChatResponseDto?> TryAnswerFromServiceAsync(string message, string intent, ConversationMemory memory, CancellationToken cancellationToken)
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
        if ((normalized.Contains("basic") || normalized.Contains("simple") || normalized.Contains("not too fancy") || normalized.Contains("cheaper"))
            && services.Any(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase)))
        {
            var bath = services.First(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase));
            memory.LastServiceName = bath.Name;
            return new ChatResponseDto(
                $"If you want something basic and not too fancy, {bath.Name} is a good fit. It takes about {bath.DurationMinutes} minutes and costs NZD {bath.Price:0.00}.",
                "asking_about_service");
        }

        if (AskingServiceMenu(normalized))
        {
            var menu = services
                .OrderBy(s => s.SortOrder)
                .ThenBy(s => s.Name)
                .Select(s => $"- {s.Name}: NZD {s.Price:0.00} ({s.DurationMinutes} min)")
                .ToList();
            if (menu.Count == 0)
            {
                return new ChatResponseDto("No active services are available right now.", "service_menu");
            }

            return new ChatResponseDto(
                "Here is what we offer right now — prices and typical duration:\n" + string.Join("\n", menu) +
                "\n\nTell me your pet and what you are hoping for (quick refresh vs full groom), and I can point you to the best fit.",
                "service_menu");
        }

        var matched = MatchService(services, normalized);
        if (matched is null && (normalized.Contains("that") || normalized.Contains("it")))
        {
            matched = services.FirstOrDefault(s => s.Name.Equals(memory.LastServiceName, StringComparison.OrdinalIgnoreCase));
        }

        if (matched is null)
        {
            return null;
        }

        memory.LastServiceName = matched.Name;

        // 预约/查空位交给 Gemini + 工具，避免仅因提到服务名就返回固定介绍。
        // 若用户明确在问价（含「怎么收费」等），仍返回本地价格。
        var askingPrice = IsPriceIntent(normalized);
        if (intent is "booking" or "availability")
        {
            if (!askingPrice)
            {
                return null;
            }
        }

        if (askingPrice || intent == "price")
        {
            return new ChatResponseDto(
                $"{matched.Name} is currently priced at NZD {matched.Price:0.00}. The service duration is about {matched.DurationMinutes} minutes.",
                "price");
        }

        return new ChatResponseDto(
            $"{matched.Name} takes around {matched.DurationMinutes} minutes. You can book it directly on the Booking page.",
            "service");
    }

    private static string DetectIntent(string message)
    {
        var normalized = message.ToLowerInvariant();
        if (IsAvailabilityIntent(normalized))
        {
            return "availability";
        }

        // 问价优先于「预约」：同句里常有「想预定 + 怎么收费」。
        if (IsPriceIntent(normalized))
        {
            return "price";
        }

        if (normalized.Contains("book") || normalized.Contains("booking") || normalized.Contains("reserve") ||
            normalized.Contains("appointment") || normalized.Contains("schedule") || normalized.Contains("make a reservation") ||
            normalized.Contains("预约") || normalized.Contains("预定") || normalized.Contains("订位"))
        {
            return "booking";
        }

        // "I wanna Wash & Tidy" has no "book" but is clearly choosing a bookable service.
        if (normalized.Contains("wanna") || normalized.Contains("want to"))
        {
            if (normalized.Contains("tidy") || normalized.Contains("groom") || normalized.Contains("full groom") ||
                normalized.Contains("wash &") || normalized.Contains("bath &") || normalized.Contains("wash&tidy") ||
                normalized.Contains("bath&tidy"))
            {
                return "booking";
            }
        }

        if (normalized.Contains("faq") || normalized.Contains("question") || normalized.Contains("常见问题") ||
            normalized.Contains("policy") || normalized.Contains("policies"))
        {
            return "faq";
        }

        return "general";
    }

    /// <summary>
    /// 与 <see cref="TryAnswerFromServiceAsync"/> 使用同一套规则，避免「how much money」「怎么收费」走成普通介绍。
    /// </summary>
    private static bool IsPriceIntent(string normalizedLower)
    {
        if (normalizedLower.Contains("price") || normalizedLower.Contains("cost") || normalizedLower.Contains("pricing"))
        {
            return true;
        }

        if (normalizedLower.Contains("how much") || normalizedLower.Contains("how much money"))
        {
            return true;
        }

        if (normalizedLower.Contains(" money") || normalizedLower.StartsWith("money ") || normalizedLower == "money")
        {
            return true;
        }

        if (normalizedLower.Contains("fee") || normalizedLower.Contains("fees") || normalizedLower.Contains("charge") || normalizedLower.Contains("charges"))
        {
            return true;
        }

        return normalizedLower.Contains("多少钱")
            || normalizedLower.Contains("价格")
            || normalizedLower.Contains("收费")
            || normalizedLower.Contains("费用")
            || normalizedLower.Contains("价钱");
    }

    /// <summary>
    /// 先按库里的英文名包含匹配，再用常见说法/中文别名匹配（如「洗狗」→ Bath &amp; Tidy）。
    /// </summary>
    private static string FoldAlnum(string s) =>
        string.Concat(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));

    private static Service? MatchService(IReadOnlyList<Service> services, string normalizedLower)
    {
        var msgFold = FoldAlnum(normalizedLower);

        foreach (var svc in services.OrderByDescending(x => FoldAlnum(x.Name).Length))
        {
            var key = FoldAlnum(svc.Name);
            if (key.Length < 3)
            {
                continue;
            }

            if (msgFold.Contains(key, StringComparison.Ordinal))
            {
                return svc;
            }

            if (msgFold.Length >= 6 && key.Contains(msgFold, StringComparison.Ordinal))
            {
                return svc;
            }
        }

        var byName = services.FirstOrDefault(x => normalizedLower.Contains(x.Name.ToLowerInvariant()));
        if (byName is not null)
        {
            return byName;
        }

        var hintRows = new (string ServiceName, string[] Phrases)[]
        {
            (
                "Full Groom",
                ["full grooming package", "full grooming", "grooming package", "full groom package"]
            ),
            (
                "Bath & Tidy",
                [
                    "wash & tidy", "wash and tidy", "wash tidy", "wash&tidy", "washtidy",
                    "bath&tidy", "bathtidy", "bath & tidy", "bath and tidy", "bath tidy", "洗狗", "洗澡", "wash and tidy"
                ]
            )
        };

        foreach (var (serviceName, phrases) in hintRows)
        {
            foreach (var phrase in phrases.OrderByDescending(p => p.Length))
            {
                var phraseFold = FoldAlnum(phrase);
                if (phraseFold.Length >= 4 && msgFold.Contains(phraseFold, StringComparison.Ordinal))
                {
                    return services.FirstOrDefault(s => s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
                }

                if (normalizedLower.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                {
                    return services.FirstOrDefault(s => s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        return null;
    }

    private static int ScoreFaq(
        string normalizedMessage,
        HashSet<string> messageTerms,
        string normalizedQuestion,
        string normalizedAnswer)
    {
        var score = 0;

        if (normalizedQuestion.Contains(normalizedMessage, StringComparison.Ordinal))
        {
            score += 8;
        }

        if (normalizedMessage.Contains(normalizedQuestion, StringComparison.Ordinal))
        {
            score += 6;
        }

        var qTerms = ExtractTerms(normalizedQuestion);
        var aTerms = ExtractTerms(normalizedAnswer);

        foreach (var term in messageTerms)
        {
            if (qTerms.Contains(term))
            {
                score += 2;
            }
            else if (aTerms.Contains(term))
            {
                score += 1;
            }
        }

        return score;
    }

    private static string NormalizeText(string text)
    {
        var chars = text
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? ch : ' ')
            .ToArray();
        return string.Join(' ', new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static HashSet<string> ExtractTerms(string normalizedText)
    {
        return normalizedText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExpandTerms(HashSet<string> terms)
    {
        var expanded = new HashSet<string>(terms, StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            if (Synonyms.TryGetValue(term, out var list))
            {
                foreach (var synonym in list)
                {
                    expanded.Add(synonym);
                }
            }
        }

        return expanded;
    }

    private static bool AskingServiceMenu(string normalizedLower)
    {
        var compact = string.Concat(normalizedLower.Where(ch => !char.IsWhiteSpace(ch)));
        return normalizedLower.Contains("service menu")
            || normalizedLower.Contains("whole service")
            || normalizedLower.Contains("all services")
            || normalizedLower.Contains("what services")
            || normalizedLower.Contains("services do you offer")
            || normalizedLower.Contains("show me everything")
            || (normalizedLower.Contains("what service") &&
                (normalizedLower.Contains("you have") || normalizedLower.Contains("doyou have") || normalizedLower.Contains("do i pick") || normalizedLower.Contains("can i choose")))
            || (compact.Contains("whatservice") && compact.Contains("doyouhave"))
            || normalizedLower.Contains("list of services")
            || normalizedLower.Contains("which services")
            || normalizedLower.Contains("menu");
    }

    private static bool IsConfirmationMessage(string normalizedLower) =>
        normalizedLower.TrimStart().StartsWith("yes, confirm ", StringComparison.OrdinalIgnoreCase)
        || BookingConfirmationRegex.IsMatch(normalizedLower);

    private static bool IsAvailabilityIntent(string normalizedLower) =>
        normalizedLower.Contains("availability")
        || normalizedLower.Contains("available")
        || normalizedLower.Contains("are you free")
        || normalizedLower.Contains("free tomorrow")
        || normalizedLower.Contains("busy tomorrow")
        || normalizedLower.Contains("can i come tomorrow")
        || normalizedLower.Contains("time slot")
        || normalizedLower.Contains("free slot")
        || normalizedLower.Contains("check availability")
        || normalizedLower.Contains("tomorrow afternoon")
        || normalizedLower.Contains("tomorrow morning")
        || normalizedLower.Contains("any time tomorrow")
        || normalizedLower.Contains("around noon")
        || normalizedLower.Contains("around lunchtime")
        || (normalizedLower.Contains("明天") && (normalizedLower.Contains("洗") || normalizedLower.Contains("wash") || normalizedLower.Contains("dog")))
        || normalizedLower.Contains("有空");

    /// <summary>Thanks, goodbye, small talk, short greetings — only when intent is not already business-focused.</summary>
    private static ChatResponseDto? TryReplyConversationalCue(string normalized, string intent)
    {
        if (intent is "booking" or "availability" or "price" or "faq")
        {
            return null;
        }

        if (IsThanksMessage(normalized))
        {
            var flip = normalized.Length % 2 == 0;
            return flip
                ? new ChatResponseDto(
                    "You are so welcome — glad I could help. Whenever you are ready to book or check a date, just say the word.",
                    "social")
                : new ChatResponseDto(
                    "Happy to help! If anything else comes up — pricing, a nervous pet, or picking a time — I am here.",
                    "social");
        }

        if (IsGoodbyeMessage(normalized))
        {
            return new ChatResponseDto(
                "Take care — hope to see you and your pet soon. You can pop back into chat anytime.",
                "social");
        }

        if (IsHowAreYouMessage(normalized))
        {
            return new ChatResponseDto(
                "I am doing great, thanks for asking — and I am here to make things easy. " +
                "Would you like the service menu, a quick price check, open times on a date, or help starting a booking?",
                "social");
        }

        if (IsCasualGreetingMessage(normalized))
        {
            return new ChatResponseDto(
                "Hi there — lovely to meet you. I can show our grooming menu and prices, check availability on your date, or guide you through a booking step by step.\n\n" +
                "What would you like to do first?",
                "greeting");
        }

        return null;
    }

    private static bool IsThanksMessage(string normalized)
    {
        if (normalized.Length > 120)
        {
            return false;
        }

        return normalized.Contains("thank you", StringComparison.Ordinal) ||
               normalized.Contains("thanks", StringComparison.Ordinal) ||
               normalized.Contains("cheers", StringComparison.Ordinal) ||
               normalized.Contains("much appreciated", StringComparison.Ordinal) ||
               normalized.Contains("thx", StringComparison.Ordinal) ||
               normalized.Trim() is "ty" ||
               normalized.Contains("谢谢", StringComparison.Ordinal);
    }

    private static bool IsGoodbyeMessage(string normalized)
    {
        if (normalized.Length > 80)
        {
            return false;
        }

        var t = normalized.Trim();
        return t is "bye" or "goodbye" or "see you" or "cya" or "ttyl" ||
               t.StartsWith("bye ", StringComparison.Ordinal) ||
               normalized.Contains("see you later", StringComparison.Ordinal) ||
               normalized.Contains("have a good day", StringComparison.Ordinal) ||
               normalized.Contains("have a great day", StringComparison.Ordinal) ||
               normalized.Contains("再见", StringComparison.Ordinal);
    }

    private static bool IsHowAreYouMessage(string normalized)
    {
        if (normalized.Length > 88)
        {
            return false;
        }

        return normalized.Contains("how are you", StringComparison.Ordinal) ||
               normalized.Contains("how're you", StringComparison.Ordinal) ||
               normalized.Contains("how r u", StringComparison.Ordinal) ||
               normalized.Contains("hows it going", StringComparison.Ordinal) ||
               normalized.Contains("how's it going", StringComparison.Ordinal) ||
               normalized.Trim() is "what's up" or "whats up" or "sup" ||
               normalized.StartsWith("how have you been", StringComparison.Ordinal);
    }

    private static bool IsCasualGreetingMessage(string normalized)
    {
        var t = normalized.Trim();
        if (t.Length == 0 || t.Length > 52)
        {
            return false;
        }

        return t is "hi" or "hello" or "hey" or "hiya" or "yo" or "heya" or "good morning" or "good afternoon" or
               "good evening" or "morning" or "afternoon" or "evening" or "你好" or "您好" or "hi there" or "hey there" or
               "hello there";
    }

    private static ConversationStage DetectStage(string normalized, string intent, bool bookingInProgress)
    {
        if (IsComparisonQuestion(normalized))
        {
            return ConversationStage.comparing_services;
        }

        if (IsServiceQuestion(normalized) || IsRecommendationQuestion(normalized) || IsGroomingNeedStatement(normalized))
        {
            return ConversationStage.asking_about_service;
        }

        if (bookingInProgress)
        {
            return ConversationStage.booking_in_progress;
        }

        if (intent is "booking" or "availability")
        {
            return ConversationStage.ready_to_book;
        }

        return ConversationStage.browsing;
    }

    private async Task<ChatResponseDto?> TryAnswerServiceAdvisoryAsync(
        string message,
        string normalized,
        ConversationStage stage,
        ConversationMemory memory,
        CancellationToken cancellationToken)
    {
        if (normalized.Contains("how does booking work"))
        {
            return new ChatResponseDto(
                "Booking is simple: pick a service and date, then share your name, phone, and pet name. I send a confirmation summary first, and only confirm after your explicit \"Yes, confirm <token>\".",
                "faq");
        }

        if (normalized.Contains("safe for nervous"))
        {
            return new ChatResponseDto(
                "Yes - we can keep it low-stress for nervous pets. Bath & Tidy is usually the gentler starting option, and we can go slowly with breaks if needed.",
                "asking_about_service");
        }

        if (normalized.Contains("actually not a cat") || normalized.Contains("have a dog actually"))
        {
            memory.LastUserProfile = (memory.LastUserProfile + " dog").Trim();
            return new ChatResponseDto(
                "Got it - I updated that to dog. If you want, I can suggest a suitable service and check tomorrow's availability.",
                "asking_about_service");
        }

        if (normalized.Contains("what do i need to prepare"))
        {
            return new ChatResponseDto(
                "Before arrival, please make sure your pet has had a toilet break, keep your phone reachable, and let us know any skin or behavior concerns. If your pet is nervous, tell me and I can suggest a gentler option.",
                "faq");
        }

        if ((normalized == "price?" || normalized == "price" || normalized == "how much") && string.IsNullOrWhiteSpace(memory.LastServiceName))
        {
            return new ChatResponseDto(
                "Sure - tell me which service you want the price for (for example: Full Groom or Bath & Tidy).",
                "pricing");
        }

        if (normalized.Contains("dog") || normalized.Contains("cat") || normalized.Contains("nervous") || normalized.Contains("small") || normalized.Contains("first time"))
        {
            memory.LastUserProfile = (memory.LastUserProfile + " " + normalized).Trim();
        }

        List<Service> services;
        try
        {
            services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        }
        catch
        {
            return null;
        }

        if (services.Count == 0)
        {
            return null;
        }

        if (stage == ConversationStage.comparing_services)
        {
            var compare = BuildComparisonFacts(normalized, services);
            if (compare is null)
            {
                return null;
            }

            var system = """
                         You are a friendly pet-grooming front-desk assistant.
                         Respond naturally and briefly.
                         Style: comparison guidance.
                         Explain key differences in plain language, then recommend who each service is best for.
                         Do not ask for booking fields yet.
                         """;
            var user = $"Customer question: {message}\n\nFacts:\n{compare}";
            var reply = await geminiChat.ReplyWithGeminiTextAsync(system, user, cancellationToken);
            return new ChatResponseDto(
                reply ?? $"Here is a quick comparison:\n{compare}\nIf you want, I can help you pick one based on your pet's needs.",
                "comparing_services");
        }

        if (stage == ConversationStage.asking_about_service)
        {
            if (IsGroomingNeedStatement(normalized) &&
                !HasExplicitBookingVerb(normalized) &&
                services.Any(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase)))
            {
                var bath = services.First(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase));
                memory.LastServiceName = bath.Name;
                return new ChatResponseDto(
                    $"For a basic and low-stress clean, I recommend {bath.Name}. It is a simple refresh service ({bath.DurationMinutes} minutes, NZD {bath.Price:0.00}).",
                    "asking_about_service");
            }

            if (normalized.Contains("what do you recommend") || normalized.Contains("which one") || normalized.Contains("something simple"))
            {
                var recommendedFromMemory = RecommendService((memory.LastUserProfile + " " + normalized).Trim(), services);
                if (!string.IsNullOrWhiteSpace(recommendedFromMemory))
                {
                    memory.LastRecommendedService = recommendedFromMemory;
                    memory.LastServiceName = ServiceNameFromRecommendation(recommendedFromMemory) ?? memory.LastServiceName;
                    return new ChatResponseDto(
                        $"Based on what you told me, I would go with {recommendedFromMemory}. It is the lighter, calmer option. Want me to check tomorrow's availability for it?",
                        "asking_about_service");
                }

                if (!string.IsNullOrWhiteSpace(memory.LastRecommendedService))
                {
                    return new ChatResponseDto(
                        $"I still recommend {memory.LastRecommendedService} based on your earlier notes. Want me to check tomorrow's availability?",
                        "asking_about_service");
                }
            }

            var matched = MatchService(services, normalized);
            if (matched is null && (normalized.Contains("that") || normalized.Contains("it")))
            {
                matched = services.FirstOrDefault(s => s.Name.Equals(memory.LastServiceName, StringComparison.OrdinalIgnoreCase));
            }
            if (matched is null && IsServiceQuestion(normalized))
            {
                matched = services.FirstOrDefault(s => s.Name.Equals(memory.LastServiceName, StringComparison.OrdinalIgnoreCase));
            }
            if (matched is not null && IsServiceQuestion(normalized))
            {
                memory.LastServiceName = matched.Name;
                var fact = BuildServiceFact(matched);
                var system = """
                             You are a friendly pet-grooming front-desk assistant.
                             Respond naturally and clearly.
                             Style: consultation.
                             Explain what the service includes, who it fits, and end with a light guidance question.
                             Do not ask for full booking form details.
                             """;
                var user = $"Customer question: {message}\n\nService facts:\n{fact}";
                var reply = await geminiChat.ReplyWithGeminiTextAsync(system, user, cancellationToken);
                return new ChatResponseDto(
                    reply ?? $"Let me explain {matched.Name}: {matched.Description} It takes about {matched.DurationMinutes} minutes and costs NZD {matched.Price:0.00}.",
                    "asking_about_service");
            }

            var recommended = RecommendService(normalized, services);
            if (recommended is not null)
            {
                memory.LastRecommendedService = recommended;
                memory.LastServiceName = ServiceNameFromRecommendation(recommended) ?? memory.LastServiceName;
                var system = """
                             You are a friendly pet-grooming front-desk assistant.
                             Respond naturally and warmly.
                             Style: recommendation.
                             Start with a recommendation, explain why in 1-2 points, and offer to help check availability.
                             Do not ask for booking form details yet.
                             """;
                var user = $"Customer question: {message}\n\nRecommendation facts:\n{recommended}";
                var reply = await geminiChat.ReplyWithGeminiTextAsync(system, user, cancellationToken);
                return new ChatResponseDto(
                    reply ?? $"Based on what you shared, I recommend {recommended}. I can also help you compare options if you want.",
                    "asking_about_service");
            }

            if (normalized.Contains("too much") && services.Any(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase)))
            {
                var bath = services.First(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase));
                memory.LastServiceName = bath.Name;
                return new ChatResponseDto(
                    $"If Full Groom feels a bit much, {bath.Name} is a gentler option and usually better for a first visit. It takes around {bath.DurationMinutes} minutes.",
                    "asking_about_service");
            }

            if ((normalized.Contains("can i do tomorrow") || normalized.Contains("can i book that tomorrow") || normalized.Contains("book that tomorrow")) &&
                !string.IsNullOrWhiteSpace(memory.LastServiceName))
            {
                var parsedDate = ParseDate(normalized) ?? DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(1).ToString("yyyy-MM-dd");
                var deterministic = await TryBuildAvailabilityReplyAsync(memory.LastServiceName!, parsedDate, normalized, cancellationToken);
                if (deterministic is not null)
                {
                    return deterministic;
                }
            }
        }

        return null;
    }

    private static bool IsComparisonQuestion(string normalized) =>
        normalized.Contains("difference")
        || normalized.Contains("different between")
        || normalized.Contains("what is different")
        || normalized.Contains("what's different")
        || normalized.Contains("whats different")
        || normalized.Contains("how are they different")
        || normalized.Contains("how do they differ")
        || (normalized.Contains("different") && normalized.Contains("between") && (normalized.Contains("them") || normalized.Contains("these") || normalized.Contains("those")))
        || normalized.Contains("compare")
        || normalized.Contains(" vs ")
        || normalized.Contains("better than");

    private static bool IsRecommendationQuestion(string normalized) =>
        normalized.Contains("suitable")
        || normalized.Contains("recommend")
        || normalized.Contains("what do you recommend")
        || normalized.Contains("which service")
        || normalized.Contains("which one")
        || normalized.Contains("best for")
        || normalized.Contains("not sure what service")
        || normalized.Contains("new puppy")
        || normalized.Contains("bit dirty")
        || normalized.Contains("something quick")
        || normalized.Contains("smells bad")
        || normalized.Contains("what should i do")
        || normalized.Contains("scared")
        || normalized.Contains("not a cat")
        || normalized.Contains("not a dog")
        || normalized.Contains("first time")
        || normalized.Contains("nervous")
        || normalized.Contains("small dog")
        || normalized.Contains("small cat")
        || normalized.Contains("long hair")
        || normalized.Contains("long-haired");

    private static bool IsServiceQuestion(string normalized) =>
        normalized.Contains("what is")
        || normalized.Contains("what's")
        || normalized.Contains("how does the booking work")
        || normalized.Contains("what do i need to prepare")
        || normalized.Contains("what happens during grooming")
        || normalized.Contains("safe for nervous")
        || normalized.Contains("tell me about")
        || normalized.Contains("service")
        || normalized.Contains("include");

    /// <summary>
    /// Vague "needs a wash" — not a named menu item like "Wash &amp; Tidy" (substring "wash" would false-positive there).
    /// </summary>
    private static bool LooksLikeNamedServiceInMessage(string normalized) =>
        normalized.Contains("wash & tidy", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("bath & tidy", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("wash&tidy", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("bath&tidy", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("full groom", StringComparison.OrdinalIgnoreCase);

    private static bool HasExplicitBookingVerb(string normalized) =>
        normalized.Contains("book", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("reserve", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("appointment", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("schedule", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("预约", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("预定", StringComparison.OrdinalIgnoreCase);

    private static bool IsShortBookingAffirmation(string normalized)
    {
        var t = normalized.Trim().TrimEnd('.', '!', '?').Trim();
        return t is "yes" or "yeah" or "yep" or "sure" or "ok" or "okay" or "yup" or "ya";
    }

    private static bool IsGroomingNeedStatement(string normalized)
    {
        if (LooksLikeNamedServiceInMessage(normalized))
        {
            return false;
        }

        return normalized.Contains("clean my", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("groom my", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("wash my", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("wash the", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("need a wash", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("needs a wash", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("need to wash", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("洗", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("smells bad", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("something quick", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("just wash", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("not too fancy", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("just basic", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("something simple", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildServiceFact(Service service)
    {
        var audience = ServiceAudienceHints.TryGetValue(service.Name, out var hint)
            ? hint
            : "Suitable for general grooming needs.";
        return $"- Name: {service.Name}\n" +
               $"- Includes: {service.Description}\n" +
               $"- Typical duration: {service.DurationMinutes} minutes\n" +
               $"- Price: NZD {service.Price:0.00}\n" +
               $"- Recommended for: {audience}\n" +
               "- Not ideal when: customer wants a much shorter/longer appointment than this service offers.";
    }

    private static string? BuildComparisonFacts(string normalized, IReadOnlyList<Service> services)
    {
        var a = services.FirstOrDefault(s => normalized.Contains(s.Name.ToLowerInvariant()));
        var b = services.FirstOrDefault(s => !ReferenceEquals(s, a) && normalized.Contains(s.Name.ToLowerInvariant()));
        if (a is null || b is null)
        {
            a = services.FirstOrDefault(s => s.Name.Equals("Full Groom", StringComparison.OrdinalIgnoreCase));
            b = services.FirstOrDefault(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase));
        }

        if (a is null || b is null)
        {
            return null;
        }

        return $"{BuildServiceFact(a)}\n\n{BuildServiceFact(b)}";
    }

    private static string? RecommendService(string normalized, IReadOnlyList<Service> services)
    {
        var full = services.FirstOrDefault(s => s.Name.Equals("Full Groom", StringComparison.OrdinalIgnoreCase));
        var bath = services.FirstOrDefault(s => s.Name.Equals("Bath & Tidy", StringComparison.OrdinalIgnoreCase));

        if (normalized.Contains("long hair") || normalized.Contains("long-haired") || normalized.Contains("fluffy"))
        {
            return full is null ? null : $"{full.Name} because long-haired pets usually need fuller coat work.";
        }

        if (normalized.Contains("nervous") || normalized.Contains("first time") || normalized.Contains("small cat") || normalized.Contains("puppy") || normalized.Contains("dirty") || normalized.Contains("quick") || normalized.Contains("smells bad") || normalized.Contains("scared"))
        {
            return bath is null ? null : $"{bath.Name} because it is shorter and usually gentler for first-time or nervous pets.";
        }

        if (normalized.Contains("cat"))
        {
            return bath is null
                ? (full is null ? null : $"{full.Name} for complete grooming.")
                : $"{bath.Name} for a lighter visit, or {full?.Name ?? "Full Groom"} for complete grooming.";
        }

        return null;
    }

    private static string? ServiceNameFromRecommendation(string text)
    {
        if (text.Contains("Bath & Tidy", StringComparison.OrdinalIgnoreCase))
        {
            return "Bath & Tidy";
        }

        if (text.Contains("Full Groom", StringComparison.OrdinalIgnoreCase))
        {
            return "Full Groom";
        }

        return null;
    }

    private async Task<ChatResponseDto?> TryHandleLocalBookingFlowAsync(
        string message,
        string normalized,
        string intent,
        string sessionKey,
        ConversationMemory memory,
        CancellationToken cancellationToken)
    {
        var hasDraft = BookingDrafts.TryGetValue(sessionKey, out var existing);
        if (hasDraft && IsCancelFlowMessage(normalized))
        {
            BookingDrafts.TryRemove(sessionKey, out _);
            return new ChatResponseDto(
                "No problem - I have cancelled the current booking flow. We can start fresh anytime.",
                "browsing");
        }

        if (IsConfirmationMessage(normalized))
        {
            return null;
        }

        if (!hasDraft && intent is not ("booking" or "availability") && !LooksLikeBookingFragment(normalized))
        {
            return null;
        }

        var draft = existing ?? new BookingDraft();
        if (intent == "availability")
        {
            draft.Mode = "availability";
        }
        else if (intent == "booking")
        {
            draft.Mode = "booking";
        }
        else if (!hasDraft && LooksLikeBookingFragment(normalized))
        {
            draft.Mode = "booking";
        }

        if (hasDraft &&
            string.Equals(draft.Mode, "availability", StringComparison.OrdinalIgnoreCase) &&
            intent != "availability" &&
            !LooksLikeBookingFragment(normalized))
        {
            BookingDrafts.TryRemove(sessionKey, out _);
            return null;
        }

        var services = await dataAccess.GetActiveServicesAsync(cancellationToken);
        var matched = MatchService(services, normalized);
        if (matched is not null)
        {
            draft.ServiceName = matched.Name;
            memory.LastServiceName = matched.Name;
        }

        if (hasDraft &&
            string.Equals(draft.Mode, "booking", StringComparison.OrdinalIgnoreCase) &&
            intent is not ("booking" or "availability") &&
            IsConversationSwitch(normalized) &&
            !IsComparisonQuestion(normalized) &&
            matched is null)
        {
            BookingDrafts.TryRemove(sessionKey, out _);
            return new ChatResponseDto(
                "No problem, we can pause booking for now. Ask anything about services, pricing, or recommendations.",
                "browsing");
        }

        if (hasDraft &&
            string.Equals(draft.Mode, "availability", StringComparison.OrdinalIgnoreCase) &&
            (IsConversationSwitch(normalized) || IsServiceQuestion(normalized) || IsRecommendationQuestion(normalized) || IsPriceIntent(normalized)) &&
            matched is null)
        {
            BookingDrafts.TryRemove(sessionKey, out _);
            return null;
        }

        ParseLooseBookingFields(message, normalized, draft, services);

        if (normalized.Contains("yesterday") || normalized.Contains("昨天"))
        {
            return new ChatResponseDto(
                "I cannot book past dates. Please share today or a future date (for example: tomorrow or 2026-04-02).",
                "booking");
        }

        if (AnyClockRegex.IsMatch(normalized) && ParseTime(normalized) is null)
        {
            return new ChatResponseDto(
                "That time looks invalid. Please use a valid time like 09:30, 14:00, or 6:30pm.",
                "booking");
        }

        var date = ParseDate(normalized);
        if (date is not null)
        {
            draft.Date = date;
        }

        var time = ParseTime(StripDatePatternsForTimeParse(normalized));
        if (time is not null)
        {
            draft.StartTime = time;
        }

        var nameMatch = NameRegex.Match(message);
        if (nameMatch.Success)
        {
            draft.CustomerName = nameMatch.Groups[1].Value.Trim();
        }
        else
        {
            var looseName = LooseNameRegex.Match(message);
            if (looseName.Success)
            {
                draft.CustomerName = looseName.Groups[1].Value.Trim();
            }
        }

        var phoneMatch = PhoneRegex.Match(message);
        if (phoneMatch.Success)
        {
            draft.Phone = NormalizePhone(phoneMatch.Value);
        }

        var petNameMatch = PetNameRegex.Match(message);
        if (petNameMatch.Success)
        {
            draft.PetName = petNameMatch.Groups[1].Value.Trim();
        }
        var dogCatNameMatch = DogCatNameRegex.Match(message);
        if (dogCatNameMatch.Success)
        {
            draft.PetName = dogCatNameMatch.Groups[1].Value.Trim();
        }
        var petPlain = PetPlainNameRegex.Match(message);
        if (petPlain.Success)
        {
            draft.PetName = petPlain.Groups[1].Value.Trim();
        }
        var petCalled = PetCalledNameRegex.Match(message);
        if (petCalled.Success)
        {
            draft.PetName = petCalled.Groups[1].Value.Trim();
        }

        if (normalized.Contains(" dog") || normalized.StartsWith("dog ") || normalized.Contains(" my dog"))
        {
            draft.PetType = "dog";
            memory.LastUserProfile = (memory.LastUserProfile + " dog").Trim();
        }
        else if (normalized.Contains(" cat") || normalized.StartsWith("cat ") || normalized.Contains(" my cat"))
        {
            draft.PetType = "cat";
            memory.LastUserProfile = (memory.LastUserProfile + " cat").Trim();
        }

        if (normalized.Contains("nervous") || normalized.Contains("small") || normalized.Contains("first time"))
        {
            memory.LastUserProfile = (memory.LastUserProfile + " " + normalized).Trim();
        }

        ParseAtomicBookingFields(message, normalized, draft);

        if (string.Equals(draft.Mode, "booking", StringComparison.OrdinalIgnoreCase))
        {
            ParseSpaceSeparatedBookingLine(message, draft);
        }

        BookingDrafts[sessionKey] = draft;

        var mode = draft.Mode ?? (intent == "availability" ? "availability" : "booking");
        if (mode == "availability")
        {
            if (string.IsNullOrWhiteSpace(draft.ServiceName) && !string.IsNullOrWhiteSpace(memory.LastServiceName))
            {
                draft.ServiceName = memory.LastServiceName;
            }
            var missingAvailability = new List<string>();
            if (string.IsNullOrWhiteSpace(draft.ServiceName)) missingAvailability.Add("service name");
            if (string.IsNullOrWhiteSpace(draft.Date)) missingAvailability.Add("date (YYYY-MM-DD)");

            if (missingAvailability.Count > 0)
            {
                return new ChatResponseDto(
                    "Happy to check the calendar for you. I just need:\n• " +
                    string.Join("\n• ", missingAvailability) +
                    "\nYou can send both in one line (for example: Bath & Tidy on 2026-04-10).",
                    "availability");
            }

            var deterministic = await TryBuildAvailabilityReplyAsync(draft.ServiceName!, draft.Date!, normalized, cancellationToken);
            if (deterministic is not null)
            {
                return deterministic;
            }

            return null;
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.ServiceName)) missing.Add("service name");
        if (string.IsNullOrWhiteSpace(draft.Date)) missing.Add("date (YYYY-MM-DD)");
        if (string.IsNullOrWhiteSpace(draft.Phone)) missing.Add("phone number");
        if (string.IsNullOrWhiteSpace(draft.PetName)) missing.Add("pet name");

        if (missing.Count == 0 && string.IsNullOrWhiteSpace(draft.StartTime))
        {
            SyncBookingSlotOfferContext(draft, memory);
            var timeReply = await TryBuildBookingTimeSelectionReplyAsync(
                draft,
                message,
                normalized,
                memory,
                cancellationToken);
            if (timeReply is not null)
            {
                return timeReply;
            }
        }

        if (missing.Count == 0)
        {
            var payload = new BookingConfirmationPayload(
                draft.ServiceName!.Trim(),
                draft.Date!.Trim(),
                string.IsNullOrWhiteSpace(draft.StartTime) ? null : draft.StartTime.Trim(),
                CustomerNameNormalizer.Normalize(draft.CustomerName),
                draft.Phone!.Trim(),
                draft.PetName!.Trim(),
                string.IsNullOrWhiteSpace(draft.PetType) ? null : draft.PetType.Trim());
            return BookingConfirmationFormatter.ToResponse(payload);
        }

        if (normalized.Contains("just book it") || normalized.Contains("why so many questions"))
        {
            return new ChatResponseDto(
                "I would love to lock this in for you — I only need these so we can confirm the right slot and contact you if anything changes:\n• " +
                string.Join("\n• ", missing),
                "booking");
        }

        return new ChatResponseDto(
            "Lovely — we are almost there. To set up the appointment I still need:\n• " +
            string.Join("\n• ", missing) +
            "\nTip: you can paste everything in one message (service, date, your name, phone, pet name, dog or cat).",
            "booking");
    }

    private async Task<ChatResponseDto?> TryBuildAvailabilityReplyAsync(
        string serviceName,
        string dateText,
        string normalized,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(dateText, out var date))
        {
            return null;
        }

        var services = await bookingService.GetServicesAsync(cancellationToken);
        var service = services.FirstOrDefault(s => s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
                      ?? services.FirstOrDefault(s => serviceName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            return null;
        }

        var availability = await bookingService.GetAvailabilityAsync(service.Id, date, cancellationToken);
        var slots = availability.Slots.Where(s => s.IsAvailable).Select(s => s.StartTime).ToList();
        if (slots.Count == 0)
        {
            return new ChatResponseDto(
                $"I checked {service.Name} for {date:yyyy-MM-dd} and it is fully booked. I can check another date for you.",
                "availability");
        }

        var preferred = FilterPreferredSlots(slots, normalized);
        var display = preferred.Take(5).ToList();
        if (display.Count == 0)
        {
            display = slots.Take(5).ToList();
        }

        return new ChatResponseDto(
            $"We have availability for {service.Name} on {date:yyyy-MM-dd}: {string.Join(", ", display.Select(x => x.ToString("HH:mm")))}.",
            "availability");
    }

    private static IReadOnlyList<TimeOnly> FilterPreferredSlots(IReadOnlyList<TimeOnly> slots, string normalized) =>
        FilterSlotsByDayPreference(slots, normalized);

    /// <summary>
    /// 上午/下午/晚上/早上 + 中午；用于查空位与预约选时。
    /// </summary>
    private static IReadOnlyList<TimeOnly> FilterSlotsByDayPreference(IReadOnlyList<TimeOnly> slots, string normalized)
    {
        if (normalized.Contains("evening", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("night", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("晚上", StringComparison.Ordinal))
        {
            return slots.Where(x => x >= new TimeOnly(17, 0) && x <= new TimeOnly(21, 30)).ToList();
        }

        if (normalized.Contains("afternoon", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("下午", StringComparison.Ordinal))
        {
            return slots.Where(x => x >= new TimeOnly(12, 0) && x < new TimeOnly(18, 0)).ToList();
        }

        if (normalized.Contains("noon", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("中午", StringComparison.Ordinal) ||
            normalized.Contains("lunchtime", StringComparison.OrdinalIgnoreCase))
        {
            return slots.Where(x => x >= new TimeOnly(11, 0) && x <= new TimeOnly(14, 0)).ToList();
        }

        if (normalized.Contains("早上", StringComparison.Ordinal) &&
            !normalized.Contains("早上好", StringComparison.Ordinal))
        {
            return slots.Where(x => x >= new TimeOnly(7, 0) && x < new TimeOnly(11, 0)).ToList();
        }

        if (normalized.Contains("morning", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("上午", StringComparison.Ordinal))
        {
            return slots.Where(x => x < new TimeOnly(12, 0)).ToList();
        }

        return slots;
    }

    private static bool IsEarlierTimeRequest(string normalized)
    {
        if (Regex.IsMatch(normalized, @"\b(?:a\s+)?(?:bit\s+)?earlier\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(normalized, @"\bsooner\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return normalized.Contains("早一点", StringComparison.Ordinal) ||
               normalized.Contains("再早", StringComparison.Ordinal) ||
               normalized.Contains("往前", StringComparison.Ordinal) ||
               (normalized.Contains("早点", StringComparison.Ordinal) && !normalized.Contains("早上好", StringComparison.Ordinal));
    }

    private static bool IsLaterTimeRequest(string normalized)
    {
        if (Regex.IsMatch(normalized, @"\b(?:a\s+)?(?:bit\s+)?later\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return normalized.Contains("晚一点", StringComparison.Ordinal) ||
               normalized.Contains("再晚", StringComparison.Ordinal) ||
               normalized.Contains("往后", StringComparison.Ordinal) ||
               (normalized.Contains("晚点", StringComparison.Ordinal) && !normalized.Contains("晚点了吗", StringComparison.Ordinal));
    }

    private static void SyncBookingSlotOfferContext(BookingDraft draft, ConversationMemory memory)
    {
        if (!string.Equals(memory.LastBookingSlotContextDate, draft.Date, StringComparison.Ordinal) ||
            !string.Equals(memory.LastBookingSlotContextService, draft.ServiceName, StringComparison.OrdinalIgnoreCase))
        {
            memory.LastOfferedBookingSlots.Clear();
        }

        memory.LastBookingSlotContextDate = draft.Date;
        memory.LastBookingSlotContextService = draft.ServiceName;
    }

    private async Task<(bool serviceResolved, IReadOnlyList<TimeOnly> slots)> TryResolveAvailableSlotStartsAsync(
        string serviceName,
        string dateText,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(dateText, out var date))
        {
            return (false, Array.Empty<TimeOnly>());
        }

        var services = await bookingService.GetServicesAsync(cancellationToken);
        var service = services.FirstOrDefault(s => s.Name.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
                      ?? services.FirstOrDefault(s => serviceName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            return (false, Array.Empty<TimeOnly>());
        }

        var availability = await bookingService.GetAvailabilityAsync(service.Id, date, cancellationToken);
        var list = availability.Slots
            .Where(s => s.IsAvailable)
            .Select(s => s.StartTime)
            .OrderBy(t => t)
            .ToList();
        return (true, list);
    }

    private async Task<ChatResponseDto?> TryBuildBookingTimeSelectionReplyAsync(
        BookingDraft draft,
        string message,
        string normalized,
        ConversationMemory memory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(draft.ServiceName) || string.IsNullOrWhiteSpace(draft.Date))
        {
            return null;
        }

        var (resolved, allSlots) = await TryResolveAvailableSlotStartsAsync(draft.ServiceName!, draft.Date!, cancellationToken);
        if (!resolved)
        {
            return new ChatResponseDto(
                "I could not match that service on our calendar. Say **menu** for service names, then we can pick a time.",
                "booking");
        }

        if (allSlots.Count == 0)
        {
            return new ChatResponseDto(
                $"I checked **{draft.ServiceName}** on **{draft.Date}** and there are no open start times left. Want to try another date?",
                "booking");
        }

        IReadOnlyList<TimeOnly> pool = allSlots;
        var offered = memory.LastOfferedBookingSlots;

        if (IsEarlierTimeRequest(normalized) && offered.Count > 0)
        {
            var minOffered = offered.Min();
            pool = allSlots.Where(s => s < minOffered).OrderBy(s => s).ToList();
            if (pool.Count == 0)
            {
                return new ChatResponseDto(
                    $"There is nothing available **earlier** than **{minOffered:HH:mm}** on that day. " +
                    $"The earliest opening I still have is **{allSlots.Min():HH:mm}**. " +
                    "You can pick a time from the list, or say **later** for options after what I last showed.",
                    "booking");
            }
        }
        else if (IsLaterTimeRequest(normalized) && offered.Count > 0)
        {
            var maxOffered = offered.Max();
            pool = allSlots.Where(s => s > maxOffered).OrderBy(s => s).ToList();
            if (pool.Count == 0)
            {
                return new ChatResponseDto(
                    $"There is nothing available **after** **{maxOffered:HH:mm}** on that day. " +
                    $"The latest I still have is **{allSlots.Max():HH:mm}**. " +
                    "Say **earlier** if you want to see times before what I last suggested.",
                    "booking");
            }
        }
        else
        {
            var preferred = FilterSlotsByDayPreference(allSlots, normalized);
            if (preferred.Count > 0)
            {
                pool = preferred;
            }
        }

        var display = pool.Take(6).ToList();
        if (display.Count == 0)
        {
            display = allSlots.Take(6).ToList();
            memory.LastOfferedBookingSlots = display;
            var times = string.Join(", ", display.Select(t => t.ToString("HH:mm")));
            return new ChatResponseDto(
                "I could not match that time-of-day to open slots, so here are the next available starts on your date:\n" +
                $"{times}\n\n" +
                "Reply with one time (for example **10:30**), or say **morning**, **afternoon**, **evening**, or **earlier** / **later** compared to this list.",
                "booking");
        }

        memory.LastOfferedBookingSlots = display;
        var line = string.Join(", ", display.Select(t => t.ToString("HH:mm")));
        return new ChatResponseDto(
            $"Here are times that work for **{draft.ServiceName}** on **{draft.Date}**:\n**{line}**\n\n" +
            "Reply with the time you want (for example **10:30**), or say **morning**, **afternoon**, **evening**, " +
            "or **earlier** / **later** to shift from this list.",
            "booking");
    }

    private static string? ParseDate(string normalized)
    {
        var dateMatch = DateRegex.Match(normalized);
        if (dateMatch.Success)
        {
            return dateMatch.Value;
        }

        var slash = SlashOrDotDateRegex.Match(normalized);
        if (slash.Success &&
            int.TryParse(slash.Groups[1].Value, out var y) &&
            int.TryParse(slash.Groups[2].Value, out var mo) &&
            int.TryParse(slash.Groups[3].Value, out var d) &&
            mo is >= 1 and <= 12 &&
            d is >= 1 and <= 31)
        {
            try
            {
                return new DateOnly(y, mo, d).ToString("yyyy-MM-dd");
            }
            catch
            {
                // invalid calendar date
            }
        }

        var slashYy = TwoDigitYearDateRegex.Match(normalized);
        if (slashYy.Success &&
            int.TryParse(slashYy.Groups[1].Value, out var yy) &&
            int.TryParse(slashYy.Groups[2].Value, out var mo2) &&
            int.TryParse(slashYy.Groups[3].Value, out var d2) &&
            mo2 is >= 1 and <= 12 &&
            d2 is >= 1 and <= 31)
        {
            var yFull = yy < 100 ? 2000 + yy : yy;
            try
            {
                return new DateOnly(yFull, mo2, d2).ToString("yyyy-MM-dd");
            }
            catch
            {
                // invalid calendar date
            }
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        // User corrections like "tomorrow no wait friday" should follow the final day, not the first mention.
        if (normalized.Contains("no wait") && normalized.Contains("friday"))
        {
            return NextWeekday(today, DayOfWeek.Friday).ToString("yyyy-MM-dd");
        }

        if (normalized.Contains("tomorrow") || normalized.Contains("明天"))
        {
            return today.AddDays(1).ToString("yyyy-MM-dd");
        }

        if (normalized.Contains("today") || normalized.Contains("今天"))
        {
            return today.ToString("yyyy-MM-dd");
        }

        if (normalized.Contains("friday"))
        {
            return NextWeekday(today, DayOfWeek.Friday).ToString("yyyy-MM-dd");
        }

        if (normalized.Contains("next week"))
        {
            return today.AddDays(7).ToString("yyyy-MM-dd");
        }

        return null;
    }

    private static string? ParseTime(string normalized)
    {
        var strictClock = Regex.Match(normalized, @"\b(\d{1,2}):([0-5]\d)\b");
        if (strictClock.Success)
        {
            var strictHour = int.Parse(strictClock.Groups[1].Value);
            var strictMinute = int.Parse(strictClock.Groups[2].Value);
            if (strictHour is >= 0 and <= 23)
            {
                return $"{strictHour:00}:{strictMinute:00}";
            }

            return null;
        }

        if (normalized.Contains("上午"))
        {
            var mCnMorning = Regex.Match(normalized, @"上午\s*(\d{1,2})(?:[:：](\d{1,2}))?");
            if (mCnMorning.Success)
            {
                var h = int.Parse(mCnMorning.Groups[1].Value);
                var mm = mCnMorning.Groups[2].Success ? int.Parse(mCnMorning.Groups[2].Value) : 0;
                if (h is >= 0 and <= 12 && mm is >= 0 and <= 59)
                {
                    if (h == 12) h = 0;
                    return $"{h:00}:{mm:00}";
                }
            }
        }

        if (normalized.Contains("下午") || normalized.Contains("晚上"))
        {
            var mCnPm = Regex.Match(normalized, @"(?:下午|晚上)\s*(\d{1,2})(?:[:：](\d{1,2}))?");
            if (mCnPm.Success)
            {
                var h = int.Parse(mCnPm.Groups[1].Value);
                var mm = mCnPm.Groups[2].Success ? int.Parse(mCnPm.Groups[2].Value) : 0;
                if (h is >= 0 and <= 12 && mm is >= 0 and <= 59)
                {
                    if (h < 12) h += 12;
                    return $"{h:00}:{mm:00}";
                }
            }
        }

        var ampm = Regex.Match(normalized, @"\b(\d{1,2})(?::([0-5]\d))?\s*(am|pm)\b", RegexOptions.IgnoreCase);
        if (ampm.Success)
        {
            var h = int.Parse(ampm.Groups[1].Value);
            var mm = ampm.Groups[2].Success ? int.Parse(ampm.Groups[2].Value) : 0;
            var suffix = ampm.Groups[3].Value.ToLowerInvariant();
            if (h is >= 1 and <= 12)
            {
                if (suffix == "am" && h == 12) h = 0;
                if (suffix == "pm" && h < 12) h += 12;
                return $"{h:00}:{mm:00}";
            }
        }

        var atIndex = normalized.IndexOf(" at ", StringComparison.Ordinal);
        var source = atIndex >= 0 ? normalized[(atIndex + 4)..] : normalized;
        var m = TimeRegex.Match(source);
        if (!m.Success)
        {
            return null;
        }

        var hour = int.Parse(m.Groups[1].Value);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        return $"{hour:00}:{minute:00}";
    }

    private static DateOnly NextWeekday(DateOnly fromDate, DayOfWeek dayOfWeek)
    {
        var dateTime = fromDate.ToDateTime(TimeOnly.MinValue);
        var days = ((int)dayOfWeek - (int)dateTime.DayOfWeek + 7) % 7;
        if (days == 0)
        {
            days = 7;
        }

        return DateOnly.FromDateTime(dateTime.AddDays(days));
    }

    private static string? TryParseFlexibleDateIso(string text)
    {
        var m = DateRegex.Match(text);
        if (m.Success)
        {
            return m.Value;
        }

        m = SlashOrDotDateRegex.Match(text);
        if (!m.Success ||
            !int.TryParse(m.Groups[1].Value, out var y) ||
            !int.TryParse(m.Groups[2].Value, out var mo) ||
            !int.TryParse(m.Groups[3].Value, out var d) ||
            mo is < 1 or > 12 ||
            d is < 1 or > 31)
        {
            return null;
        }

        try
        {
            return new DateOnly(y, mo, d).ToString("yyyy-MM-dd");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Avoid ParseTime matching a lone digit from dates like 2026/4/2.
    /// </summary>
    private static string StripDatePatternsForTimeParse(string normalized)
    {
        var s = DateRegex.Replace(normalized, " ");
        s = SlashOrDotDateRegex.Replace(s, " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>
    /// One line without commas: e.g. "2026/4/2 coco 0248853215 bad" → date, customer, phone, pet.
    /// </summary>
    private static void ParseSpaceSeparatedBookingLine(string message, BookingDraft draft)
    {
        if (message.Contains(','))
        {
            return;
        }

        if (message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            return;
        }

        var work = message;
        work = DateRegex.Replace(work, " ");
        work = SlashOrDotDateRegex.Replace(work, " ");

        var phoneMatch = PhoneRegex.Match(work);
        if (phoneMatch.Success)
        {
            var pv = phoneMatch.Value;
            var idx = work.IndexOf(pv, StringComparison.Ordinal);
            if (idx >= 0)
            {
                work = string.Concat(work.AsSpan(0, idx), " ", work.AsSpan(idx + pv.Length));
            }

            if (string.IsNullOrWhiteSpace(draft.Phone))
            {
                draft.Phone = NormalizePhone(pv);
            }
        }

        var nameTokens = work
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2 && w.All(c => char.IsLetter(c) || c == '\'' || c == '-') && !BookingInlineNameNoise.Contains(w))
            .ToList();

        if (nameTokens.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.CustomerName) && string.IsNullOrWhiteSpace(draft.PetName))
        {
            if (nameTokens.Count >= 2)
            {
                draft.CustomerName = string.Join(" ", nameTokens.Take(nameTokens.Count - 1));
                draft.PetName = nameTokens[^1];
            }
            else
            {
                draft.CustomerName = nameTokens[0];
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(draft.PetName) && nameTokens.Count >= 1)
        {
            var candidate = nameTokens[^1];
            if (!string.Equals(candidate, draft.CustomerName, StringComparison.OrdinalIgnoreCase))
            {
                draft.PetName = candidate;
            }
        }
        else if (string.IsNullOrWhiteSpace(draft.CustomerName) && nameTokens.Count >= 1)
        {
            draft.CustomerName = nameTokens[0];
        }
    }

    private static bool IsConversationSwitch(string normalized) =>
        IsRecommendationQuestion(normalized)
        || IsComparisonQuestion(normalized)
        || IsServiceQuestion(normalized)
        || AskingServiceMenu(normalized)
        || IsPriceIntent(normalized)
        || normalized.Contains("actually wait")
        || normalized.Contains("hold on")
        || normalized.Contains("too much")
        || normalized.Contains("cheaper")
        || normalized.Contains("included")
        || normalized.Contains("faq")
        || normalized.Contains("opening hours")
        || normalized.Contains("cancel policy")
        || normalized.Contains("what can you do");

    private static bool IsCancelFlowMessage(string normalized) =>
        normalized.Contains("cancel that")
        || normalized.Contains("actually cancel")
        || normalized.Contains("never mind")
        || normalized.Contains("stop booking")
        || normalized.Contains("don't book");

    private static bool LooksLikeBookingFragment(string normalized) =>
        normalized.Contains("tomorrow")
        || normalized.Contains("today")
        || normalized.Contains("friday")
        || normalized.Contains("next week")
        || DateRegex.IsMatch(normalized)
        || SlashOrDotDateRegex.IsMatch(normalized)
        || TimeRegex.IsMatch(normalized)
        || normalized.Contains("book")
        || normalized.Contains("reserve")
        || normalized.Contains("appointment")
        || NameRegex.IsMatch(normalized);

    private static void ParseLooseBookingFields(string message, string normalized, BookingDraft draft, IReadOnlyList<Service> services)
    {
        if (!message.Contains(','))
        {
            return;
        }

        var chunks = message.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (chunks.Count == 0)
        {
            return;
        }

        foreach (var chunk in chunks)
        {
            var lower = chunk.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(draft.Phone) && PhoneRegex.IsMatch(chunk))
            {
                draft.Phone = NormalizePhone(PhoneRegex.Match(chunk).Value);
                continue;
            }

            if (string.IsNullOrWhiteSpace(draft.Date))
            {
                var isoChunk = TryParseFlexibleDateIso(lower);
                if (!string.IsNullOrWhiteSpace(isoChunk))
                {
                    draft.Date = isoChunk;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(draft.StartTime))
            {
                var t = ParseTime(lower);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    draft.StartTime = t;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(draft.ServiceName))
            {
                var m = MatchService(services, lower);
                if (m is not null)
                {
                    draft.ServiceName = m.Name;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(draft.CustomerName))
            {
                draft.CustomerName = chunk.Trim();
                continue;
            }

            if (string.IsNullOrWhiteSpace(draft.PetName))
            {
                draft.PetName = chunk.Trim();
            }
        }
    }

    private static void ParseAtomicBookingFields(string message, string normalized, BookingDraft draft)
    {
        var token = message.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.CustomerName))
        {
            var shortName = Regex.Match(token, @"^name\s+([a-z][a-z\s'\-]{1,30})$", RegexOptions.IgnoreCase);
            if (shortName.Success)
            {
                draft.CustomerName = shortName.Groups[1].Value.Trim();
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(draft.Phone) && PhoneRegex.IsMatch(token))
        {
            draft.Phone = NormalizePhone(PhoneRegex.Match(token).Value);
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.Date))
        {
            var parsedDate = ParseDate(normalized);
            if (!string.IsNullOrWhiteSpace(parsedDate))
            {
                draft.Date = parsedDate;
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(draft.StartTime))
        {
            var parsedTime = ParseTime(normalized);
            if (!string.IsNullOrWhiteSpace(parsedTime))
            {
                draft.StartTime = parsedTime;
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(draft.PetName) &&
            !string.IsNullOrWhiteSpace(draft.ServiceName) &&
            !string.IsNullOrWhiteSpace(draft.Date) &&
            token.Length is >= 2 and <= 20 &&
            !token.Contains(' ') &&
            token.All(char.IsLetter) &&
            token is not "tomorrow" and not "today" and not "dog" and not "cat" and not "groom" and not "full")
        {
            draft.PetName = token;
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.CustomerName) &&
            token.Contains(' ') &&
            token.Length <= 40 &&
            SimpleNameRegex.IsMatch(token) &&
            !token.Contains("groom", StringComparison.OrdinalIgnoreCase) &&
            !token.Contains("bath", StringComparison.OrdinalIgnoreCase) &&
            !token.Contains("tidy", StringComparison.OrdinalIgnoreCase) &&
            !token.Contains("tomorrow", StringComparison.OrdinalIgnoreCase))
        {
            draft.CustomerName = token;
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.CustomerName) &&
            !string.IsNullOrWhiteSpace(draft.Phone) &&
            token.Length is >= 2 and <= 20 &&
            !token.Contains(' ') &&
            token.All(char.IsLetter) &&
            token is not "tomorrow" and not "today" and not "book" and not "booking")
        {
            draft.CustomerName = token;
        }
    }

    private static string NormalizePhone(string phoneRaw)
    {
        var digits = new string(phoneRaw.Where(char.IsDigit).ToArray());
        return digits;
    }

    private sealed class BookingDraft
    {
        public string? Mode { get; set; }
        public string? ServiceName { get; set; }
        public string? Date { get; set; }
        public string? StartTime { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
        public string? PetName { get; set; }
        public string? PetType { get; set; }
    }

    private sealed class ConversationMemory
    {
        public string? LastIntent { get; set; }
        public string? LastServiceName { get; set; }
        public string? LastRecommendedService { get; set; }
        public string LastUserProfile { get; set; } = string.Empty;
        /// <summary>最近一轮推荐给顾客的预约开始时间（用于「早一点 / 晚一点」）。</summary>
        public List<TimeOnly> LastOfferedBookingSlots { get; set; } = new();
        public string? LastBookingSlotContextDate { get; set; }
        public string? LastBookingSlotContextService { get; set; }
    }
}
