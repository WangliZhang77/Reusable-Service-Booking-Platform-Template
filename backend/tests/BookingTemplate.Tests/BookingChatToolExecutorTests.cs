using BookingTemplate.Application.Interfaces.DataAccess;
using BookingTemplate.Application.Interfaces.Services;
using BookingTemplate.Domain.Entities;
using BookingTemplate.Infrastructure.Services;
using Google.GenAI.Types;
using Moq;
using Xunit;

namespace BookingTemplate.Tests;

public sealed class BookingChatToolExecutorTests
{
    [Fact]
    public async Task GetServicePrice_returns_not_found_when_no_match()
    {
        var data = new Mock<IBookingDataAccess>();
        data.Setup(x => x.GetActiveServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Service>());

        var booking = new Mock<IBookingService>();
        var executor = new BookingChatToolExecutor(data.Object, booking.Object);

        var result = await executor.GetServicePriceAsync("Full Groom", CancellationToken.None);
        Assert.Contains("could not find", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAvailability_returns_invalid_date_message()
    {
        var data = new Mock<IBookingDataAccess>();
        var booking = new Mock<IBookingService>();
        var executor = new BookingChatToolExecutor(data.Object, booking.Object);

        var result = await executor.CheckAvailabilityAsync("Full Groom", "not-a-date", CancellationToken.None);
        Assert.Contains("YYYY-MM-DD", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteFunctionCall_routes_GetServicePrice()
    {
        var data = new Mock<IBookingDataAccess>();
        data.Setup(x => x.GetActiveServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Service>());

        var booking = new Mock<IBookingService>();
        var executor = new BookingChatToolExecutor(data.Object, booking.Object);

        var fc = new FunctionCall
        {
            Name = "GetServicePrice",
            Args = new Dictionary<string, object> { ["serviceName"] = "X" }
        };

        var result = await executor.ExecuteFunctionCallAsync(fc, CancellationToken.None);
        Assert.Contains("could not find", result, StringComparison.OrdinalIgnoreCase);
    }
}
