using System.Globalization;

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

    [Theory]
    [InlineData(0, "less than a second")]
    [InlineData(45, "45 s")]
    [InlineData(60, "1 min")]
    [InlineData(3725, "1 h 2 min")]
    [InlineData(90061, "1 d 1 h")]
    [InlineData(3630, "1 h 30 s")]
    [InlineData(86430, "1 d 30 s")]
    [InlineData(2 * 86400 + 3 * 3600 + 59, "2 d 3 h")]
    public void Duration_is_formatted_in_english_for_an_english_culture(int seconds, string expected)
    {
        Assert.Equal(expected, DurationFormatter.Format(TimeSpan.FromSeconds(seconds), new CultureInfo("en")));
        Assert.Equal(expected, DurationFormatter.Format(TimeSpan.FromSeconds(seconds), new CultureInfo("en-GB")));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("de")]
    [InlineData("")]
    public void Any_non_english_culture_gets_russian(string name)
    {
        Assert.Equal("1 ч 2 мин", DurationFormatter.Format(TimeSpan.FromSeconds(3725), new CultureInfo(name)));
    }

    [Fact]
    public void Default_is_russian_whatever_the_thread_culture_is()
    {
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");
            Assert.Equal("1 ч 2 мин", DurationFormatter.Format(TimeSpan.FromSeconds(3725)));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }
    }

    [Fact]
    public void Negative_duration_is_treated_as_zero()
    {
        Assert.Equal("меньше секунды", DurationFormatter.Format(TimeSpan.FromSeconds(-5)));
        Assert.Equal("less than a second", DurationFormatter.Format(TimeSpan.FromSeconds(-5), new CultureInfo("en")));
    }
}
