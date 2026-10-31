using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace UptimeWatch.Web.Localization;

/// <summary>Helpers for resource strings that mix text with links or messages with the "Error:" prefix.</summary>
public static partial class LocalizerMarkup
{
    /// <summary>
    /// What the HTML encoder may write as is. The default is Latin only, so Russian page text would reach the browser
    /// as &amp;#x41C; entities (correct but unreadable in the page source and in tests).
    /// </summary>
    public static readonly TextEncoderSettings PageTextSettings = new(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic);

    private static readonly HtmlEncoder PageEncoder = HtmlEncoder.Create(PageTextSettings);

    /// <summary>
    /// The resource <paramref name="key"/> as markup: the text is HTML-encoded and <c>{0}</c>, <c>{1}</c>, ... are
    /// replaced with the given HTML snippets (see <see cref="Anchor"/>). Lets a link sit inside a translated sentence.
    /// </summary>
    public static MarkupString Html(this IStringLocalizer localizer, string key, params string[] htmlArgs)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var format = localizer[key].Value;
        var encoder = PageEncoder;
        var result = new StringBuilder();
        var position = 0;
        foreach (Match match in PlaceholderPattern().Matches(format))
        {
            result.Append(encoder.Encode(format[position..match.Index]));
            var index = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            result.Append(index < htmlArgs.Length ? htmlArgs[index] : encoder.Encode(match.Value));
            position = match.Index + match.Length;
        }

        result.Append(encoder.Encode(format[position..]));
        return new MarkupString(result.ToString());
    }

    /// <summary>An encoded <c>&lt;a&gt;</c> element for <see cref="Html"/>.</summary>
    public static string Anchor(string text, string href) =>
        $"<a href=\"{PageEncoder.Encode(href)}\">{PageEncoder.Encode(text)}</a>";

    /// <summary>An encoded <c>&lt;kbd&gt;</c> element for <see cref="Html"/>.</summary>
    public static string Kbd(string text) => $"<kbd>{PageEncoder.Encode(text)}</kbd>";

    /// <summary>
    /// The status text shown as an error: "Error: ..." in the current language. <c>StatusMessage</c> recognizes it by
    /// the same prefix.
    /// </summary>
    public static string ErrorMessage(this IStringLocalizer localizer, string key, params object[] args) =>
        localizer.WithErrorPrefix(localizer[key, args].Value);

    /// <summary>Adds the localized error prefix to a ready text (for example the joined Identity error descriptions).</summary>
    public static string WithErrorPrefix(this IStringLocalizer localizer, string text)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return $"{localizer[ErrorPrefixKey].Value} {text}";
    }

    /// <summary>True when a status text starts with the localized error prefix.</summary>
    public static bool IsErrorMessage(this IStringLocalizer localizer, string message)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return message.StartsWith(localizer[ErrorPrefixKey].Value, StringComparison.Ordinal);
    }

    public const string ErrorPrefixKey = "Common_ErrorPrefix";

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderPattern();
}
