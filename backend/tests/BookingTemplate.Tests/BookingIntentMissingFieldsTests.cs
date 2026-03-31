using BookingTemplate.Application.DTOs.Chat;
using BookingTemplate.Application.Services;
using Xunit;

namespace BookingTemplate.Tests;

public sealed class BookingIntentMissingFieldsTests
{
    [Fact]
    public void Booking_lists_all_required_when_empty()
    {
        var dto = new BookingIntentExtractionDto { Intent = "booking" };
        var missing = BookingIntentMissingFields.Compute(dto);
        Assert.Contains("serviceName", missing);
        Assert.Contains("date", missing);
        Assert.Contains("startTime", missing);
        Assert.Contains("customerName", missing);
        Assert.Contains("phone", missing);
        Assert.Contains("petName", missing);
        Assert.Contains("petType", missing);
    }

    [Fact]
    public void Booking_empty_when_all_present()
    {
        var dto = new BookingIntentExtractionDto
        {
            Intent = "booking",
            ServiceName = "Full Groom",
            Date = "2026-03-31",
            StartTime = "12:00",
            CustomerName = "Wang Li",
            Phone = "0211234567",
            PetName = "Coco",
            PetType = "dog"
        };
        var missing = BookingIntentMissingFields.Compute(dto);
        Assert.Empty(missing);
    }

    [Fact]
    public void Availability_needs_service_and_date()
    {
        var dto = new BookingIntentExtractionDto { Intent = "availability" };
        var missing = BookingIntentMissingFields.Compute(dto);
        Assert.Equal(2, missing.Count);
        Assert.Contains("serviceName", missing);
        Assert.Contains("date", missing);
    }

    [Fact]
    public void Price_needs_service_name()
    {
        var dto = new BookingIntentExtractionDto { Intent = "price" };
        var missing = BookingIntentMissingFields.Compute(dto);
        Assert.Single(missing);
        Assert.Contains("serviceName", missing);
    }

    [Fact]
    public void Faq_needs_query()
    {
        var dto = new BookingIntentExtractionDto { Intent = "faq" };
        var missing = BookingIntentMissingFields.Compute(dto);
        Assert.Single(missing);
        Assert.Contains("faqQuery", missing);
    }
}
