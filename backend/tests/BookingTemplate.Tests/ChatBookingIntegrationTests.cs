using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BookingTemplate.Application.DTOs.Chat;
using BookingTemplate.Application.Interfaces.Services;
using BookingTemplate.Infrastructure.Persistence;
using BookingTemplate.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BookingTemplate.Tests;

public sealed class ChatBookingIntegrationTests
{
    [Fact]
    public async Task Chat_booking_confirmation_then_confirm_creates_booking()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/chat", new ChatRequestDto
        {
            Message = "Book Full Groom tomorrow at 12:00 for my dog. My name is Wang Li, phone is 0211234567, pet name is Coco.",
            SessionId = "it-session"
        });
        first.EnsureSuccessStatusCode();

        var firstPayload = await first.Content.ReadFromJsonAsync<ChatResponseDto>();
        Assert.NotNull(firstPayload);
        Assert.Equal("booking_confirmation", firstPayload!.Intent);
        Assert.Contains("Yes, confirm ", firstPayload.Reply, StringComparison.OrdinalIgnoreCase);

        var token = ExtractConfirmToken(firstPayload.Reply);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var second = await client.PostAsJsonAsync("/api/chat", new ChatRequestDto
        {
            Message = $"Yes, confirm {token}",
            SessionId = "it-session"
        });
        second.EnsureSuccessStatusCode();

        var secondPayload = await second.Content.ReadFromJsonAsync<ChatResponseDto>();
        Assert.NotNull(secondPayload);
        Assert.Equal("booking", secondPayload!.Intent);
        Assert.Contains("booking is confirmed", secondPayload.Reply, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(1);
        var booking = await db.Bookings
            .Include(x => x.Service)
            .Include(x => x.Customer)
            .Include(x => x.Pet)
            .FirstOrDefaultAsync(x =>
                x.BookingDate == tomorrow &&
                x.StartTime == new TimeOnly(12, 0) &&
                x.Service.Name == "Full Groom");

        Assert.NotNull(booking);
        Assert.Equal("Wang Li", booking!.Customer.FullName);
        Assert.Equal("0211234567", booking.Customer.Phone);
        Assert.Equal("Coco", booking.Pet.Name);
    }

    private static string ExtractConfirmToken(string reply)
    {
        const string prefix = "Yes, confirm ";
        var idx = reply.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return string.Empty;
        }

        var token = reply[(idx + prefix.Length)..].Trim();
        var firstLine = token.Split('\n', '\r', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine?.Trim() ?? string.Empty;
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                _connection.Open();

                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

                services.RemoveAll<IGeminiChatService>();
                services.AddScoped<IGeminiChatService, FakeGeminiChatService>();

                using var scope = services.BuildServiceProvider().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
                DbInitializer.SeedDemoDataIfEmptyAsync(db).GetAwaiter().GetResult();
                db.Faqs.RemoveRange(db.Faqs);
                db.SaveChanges();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _connection.Dispose();
            }
        }
    }

    private sealed class FakeGeminiChatService(BookingChatToolExecutor toolExecutor) : IGeminiChatService
    {
        public async Task<ChatResponseDto?> ReplyWithGeminiAndToolsAsync(string message, string? sessionId, CancellationToken cancellationToken)
        {
            if (TryParseConfirmation(message, out var pending))
            {
                var created = await toolExecutor.CreateBookingAsync(
                    pending.ServiceName,
                    pending.Date,
                    pending.StartTime,
                    pending.CustomerName,
                    pending.Phone,
                    pending.PetName,
                    pending.PetType,
                    cancellationToken);
                return new ChatResponseDto(created, "booking");
            }

            if (message.Contains("Book Full Groom", StringComparison.OrdinalIgnoreCase))
            {
                var details = new BookingConfirmationPayload(
                    "Full Groom",
                    DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(1).ToString("yyyy-MM-dd"),
                    "12:00",
                    "Wang Li",
                    "0211234567",
                    "Coco",
                    "dog");
                return BookingConfirmationFormatter.ToResponse(details);
            }

            return new ChatResponseDto("I could not process this test message.", "general");
        }

        public Task<string?> ReplyWithGeminiTextAsync(string systemInstruction, string userMessage, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>("I can help explain services and recommend an option based on your pet's needs.");
        }

        private static readonly Regex ConfirmationRegex = new(
            @"yes, confirm\s+([A-Za-z0-9+/=]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool TryParseConfirmation(string message, out BookingConfirmationPayload pending)
        {
            pending = default!;
            var match = ConfirmationRegex.Match(message.Trim());
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
                var bytes = Convert.FromBase64String(token);
                var parsed = JsonSerializer.Deserialize<BookingConfirmationPayload>(bytes);
                if (parsed is null)
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
    }
}
