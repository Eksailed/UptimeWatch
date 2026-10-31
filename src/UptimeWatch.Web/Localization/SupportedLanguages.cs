using System.Globalization;

namespace UptimeWatch.Web.Localization;

/// <summary>
/// The UI languages. To add one: add a Resources/SharedResource.&lt;code&gt;.resx with every key and add the
/// code here (the culture cookie whitelist, the request localization and the switcher all read this list).
/// </summary>
public static class SupportedLanguages
{
    public const string Default = "ru";

    public static readonly IReadOnlyList<Language> All = [new("ru", "RU"), new("en", "EN")];

    public static string[] Codes => All.Select(l => l.Code).ToArray();

    /// <summary>The language code of the current UI culture (two letters, lower case).</summary>
    public static string Current => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    /// <summary>Returns the supported code that equals <paramref name="code"/> (ignoring case), or null.</summary>
    public static string? Find(string? code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code;
}

public sealed record Language(string Code, string Label);
