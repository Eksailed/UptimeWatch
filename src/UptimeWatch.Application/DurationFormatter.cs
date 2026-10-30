using System.Globalization;

namespace UptimeWatch.Application;

/// <summary>Human-readable durations for alerts and the UI.</summary>
public static class DurationFormatter
{
    /// <summary>
    /// Russian, at most the two largest non-zero units, e.g. "1 ч 5 мин", "45 с". Zero units in between are skipped,
    /// so 1 day 0 h 0 min 30 s reads "1 д 30 с" (each number carries its own unit, so this is unambiguous).
    /// </summary>
    public static string Format(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var parts = new List<string>(4);
        if (duration.Days > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Days} д"));
        }

        if (duration.Hours > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Hours} ч"));
        }

        if (duration.Minutes > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes} мин"));
        }

        if (duration.Seconds > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{duration.Seconds} с"));
        }

        return parts.Count == 0 ? "меньше секунды" : string.Join(' ', parts.Take(2));
    }
}
