using System.Globalization;
using AIConsole.Converters;

namespace AIConsole.Tests;

public class ActivityTimeConverterTests
{
    private static object? Convert(DateTimeOffset? value) =>
        ActivityTimeConverter.Instance.Convert(value!, typeof(string), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void NullTimestamp_ReturnsEmptyString()
    {
        Assert.Equal("", Convert(null));
    }

    [Fact]
    public void Today_ShowsTime()
    {
        var today = new DateTimeOffset(DateTime.Today.AddHours(13).AddMinutes(4));

        Assert.Equal("13:04", Convert(today));
    }

    [Fact]
    public void Yesterday_ShowsYesterday()
    {
        var yesterday = new DateTimeOffset(DateTime.Today.AddDays(-1).AddHours(9));

        Assert.Equal("Yesterday", Convert(yesterday));
    }

    [Fact]
    public void PreviousYear_ShowsFullDate()
    {
        var old = new DateTimeOffset(DateTime.Today.AddYears(-1).AddDays(-3));

        Assert.Equal(old.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Convert(old));
    }
}
