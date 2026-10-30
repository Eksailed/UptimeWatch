using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace UptimeWatch.Infrastructure.Persistence;

/// <summary>
/// Npgsql refuses non-UTC DateTime values for timestamptz. Everything the application stores is UTC,
/// so unspecified values are treated as UTC and local values are converted.
/// </summary>
internal static class UtcDateTime
{
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

internal sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => UtcDateTime.Normalize(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

internal sealed class NullableUtcDateTimeConverter()
    : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? UtcDateTime.Normalize(v.Value) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
