using System.Globalization;

namespace UptimeWatch.Web.Localization;

/// <summary>Culture-aware formatting of the values the pages show.</summary>
public static class UiFormat
{
    /// <summary>A UTC time in the format of the current culture (general date, long time), with a " UTC" suffix unless <paramref name="withZone"/> is false.</summary>
    public static string Time(DateTime utc, bool withZone = true)
    {
        var text = utc.ToString("G", CultureInfo.CurrentCulture);
        return withZone ? text + " UTC" : text;
    }
}
