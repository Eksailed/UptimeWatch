using System.Globalization;

namespace UptimeWatch.Application;

/// <summary>Human-readable durations for alerts and the UI.</summary>
public static class DurationFormatter
{
    private static readonly string[] RussianUnits = ["д", "ч", "мин", "с"];
    private static readonly string[] EnglishUnits = ["d", "h", "min", "s"];

    /// <summary>
    /// At most the two largest non-zero units, e.g. "1 ч 5 мин", "45 с". Zero units in between are skipped,
    /// so 1 day 0 h 0 min 30 s reads "1 д 30 с" (each number carries its own unit, so this is unambiguous).
    /// The language is the one of <paramref name="culture"/> when it is English ("1 d 30 s", "less than a second");
    /// anything else, and the default, is Russian. It never reads the thread culture, so alerts stay Russian
    /// whatever language the request that triggered them used.
    /// </summary>
    public static string Format(TimeSpan duration, CultureInfo? culture = null)
    {
        var english = culture?.TwoLetterISOLanguageName == "en";
        var units = english ? EnglishUnits : RussianUnits;

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var parts = new List<string>(4);
        Add(parts, duration.Days, units[0]);
        Add(parts, duration.Hours, units[1]);
        Add(parts, duration.Minutes, units[2]);
        Add(parts, duration.Seconds, units[3]);

        return parts.Count == 0
            ? english ? "less than a second" : "меньше секунды"
            : string.Join(' ', parts.Take(2));
    }

    private static void Add(List<string> parts, int value, string unit)
    {
        if (value > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{value} {unit}"));
        }
    }
}
