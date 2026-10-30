namespace UptimeWatch.Domain;

public sealed class DomainException(string message) : Exception(message);
