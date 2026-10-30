using System.Net;
using System.Net.Sockets;
using UptimeWatch.Domain;

namespace UptimeWatch.Infrastructure.Checks;

public interface IDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct);
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) =>
        Dns.GetHostAddressesAsync(host, ct);
}

public sealed class BlockedAddressException(string message) : Exception(message);

public static class AddressGuard
{
    /// <summary>
    /// Resolves <paramref name="host"/> and returns its addresses. An IP literal is checked directly
    /// (the resolver is not called). If ANY address is not public, throws <see cref="BlockedAddressException"/>.
    /// </summary>
    public static async Task<IPAddress[]> ResolveAllowedAsync(
        string host,
        IDnsResolver dns,
        bool allowPrivate,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(dns);

        var trimmed = host.Trim().TrimEnd('.').Trim('[', ']');

        IPAddress[] addresses;
        if (IPAddress.TryParse(trimmed, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await dns.ResolveAsync(trimmed, ct).ConfigureAwait(false);
            if (addresses.Length == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }
        }

        if (!allowPrivate && addresses.Any(a => !IpAddressPolicy.IsPublic(a)))
        {
            throw new BlockedAddressException("Host resolves to a blocked (non-public) address.");
        }

        return addresses;
    }
}
