namespace UptimeWatch.Domain;

/// <summary>
/// A validation error: a stable <paramref name="Code"/> plus the values to put into its message
/// (<paramref name="Args"/>, positional like <c>{0}</c>). <paramref name="Message"/> is an English text for logs and
/// exceptions only; the UI shows the text it looks up by <paramref name="Code"/> in the user's language.
/// </summary>
public sealed record ErrorInfo(ErrorCode Code, string Message, params object[] Args);
