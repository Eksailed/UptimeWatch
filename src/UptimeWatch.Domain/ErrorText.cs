namespace UptimeWatch.Domain;

/// <summary>Bounds the length of error texts before they are stored, so DB columns stay small whatever the checked site returns.</summary>
public static class ErrorText
{
    /// <summary>Returns the text unchanged if it fits <see cref="MonitorLimits.MaxErrorLength"/>, otherwise cuts it and appends "…".</summary>
    public static string? Truncate(string? error)
    {
        if (error is null || error.Length <= MonitorLimits.MaxErrorLength)
        {
            return error;
        }

        var cut = MonitorLimits.MaxErrorLength - 1;
        if (char.IsHighSurrogate(error[cut - 1]))
        {
            cut--; // never split a surrogate pair
        }

        return error[..cut] + "…";
    }
}
