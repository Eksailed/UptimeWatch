namespace UptimeWatch.Domain;

public sealed class DomainException(ErrorInfo error) : Exception(error.Message)
{
    public ErrorInfo Error { get; } = error;
}
