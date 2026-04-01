using BookingTemplate.Application.Services;
using Xunit;

namespace BookingTemplate.Tests;

public sealed class CustomerNameNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("a", null)]
    [InlineData("afternoon", null)]
    [InlineData("Afternoon", null)]
    [InlineData("have", null)]
    [InlineData("11:30", null)]
    [InlineData("0211234567", null)]
    [InlineData("please thanks", null)]
    public void Normalize_returns_null_for_placeholders(string? raw, string? expected)
    {
        Assert.Equal(expected, CustomerNameNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData("Wang Li", "Wang Li")]
    [InlineData("  Jane Doe  ", "Jane Doe")]
    public void Normalize_keeps_real_names(string raw, string expected)
    {
        Assert.Equal(expected, CustomerNameNormalizer.Normalize(raw));
    }
}
