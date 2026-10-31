using Microsoft.Extensions.Localization;
using UptimeWatch.Domain;

namespace UptimeWatch.Web.Localization;

public static class ErrorInfoLocalizer
{
    /// <summary>The resource key of an error code, e.g. <c>Error_UrlTooLong</c>.</summary>
    public static string Key(ErrorCode code) => $"Error_{code}";

    /// <summary>The error text in the current UI language; the error arguments fill <c>{0}</c>, <c>{1}</c>, ...</summary>
    public static string Text(this IStringLocalizer localizer, ErrorInfo error)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(error);
        var text = localizer[Key(error.Code), error.Args];
        return text.ResourceNotFound ? error.Message : text.Value;
    }
}
