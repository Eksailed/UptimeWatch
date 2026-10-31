namespace UptimeWatch.Domain;

/// <summary>
/// Stable identifiers of the validation errors the UI can show. The names are used as resource keys
/// (<c>Error_&lt;Name&gt;</c>) by the Web layer, so renaming a member means renaming its resource keys.
/// </summary>
public enum ErrorCode
{
    OwnerRequired,
    UrlRequired,
    UrlTooLong,
    UrlNotAbsolute,
    UrlSchemeNotAllowed,
    UrlHasCredentials,
    UrlNoHost,
    UrlLocalAddress,
    UrlPrivateAddress,
    IntervalTooShort,
    ExpectedStatusOutOfRange,
    MonitorLimitReached,
    TelegramChatIdInvalid
}
