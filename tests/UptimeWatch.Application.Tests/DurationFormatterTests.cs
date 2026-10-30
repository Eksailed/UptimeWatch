namespace UptimeWatch.Application.Tests;

public sealed class DurationFormatterTests
{
    [Theory]
    [InlineData(0, "меньше секунды")]
    [InlineData(45, "45 с")]
    [InlineData(60, "1 мин")]
    [InlineData(3725, "1 ч 2 мин")]
    [InlineData(90061, "1 д 1 ч")]
    [InlineData(3630, "1 ч 30 с")]
    [InlineData(86430, "1 д 30 с")] // 1 d 0 h 0 min 30 s: zero units are skipped, not shown as "1 д 0 ч"
    [InlineData(86700, "1 д 5 мин")]
    [InlineData(2 * 86400 + 3 * 3600 + 59, "2 д 3 ч")] // the incident table must not drop the days
    public void Duration_is_formatted_in_russian_with_two_largest_units(int seconds, string expected)
    {
        Assert.Equal(expected, DurationFormatter.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Negative_duration_is_treated_as_zero()
    {
        Assert.Equal("меньше секунды", DurationFormatter.Format(TimeSpan.FromSeconds(-5)));
    }
}
