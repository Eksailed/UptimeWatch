using System.Net;
using System.Net.Sockets;

namespace UptimeWatch.Domain;

public static class IpAddressPolicy
{
    /// <summary>True only for globally routable unicast addresses.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            else
            {
                return IsPublicV6(address);
            }
        }

        return address.AddressFamily == AddressFamily.InterNetwork && IsPublicV4(address.GetAddressBytes());
    }

    private static bool IsPublicV4(ReadOnlySpan<byte> b)
    {
        return !(
            b[0] == 0                                        // 0.0.0.0/8 "this network"
            || b[0] == 10                                    // 10.0.0.0/8
            || (b[0] == 100 && (b[1] & 0xC0) == 64)          // 100.64.0.0/10 CGNAT
            || b[0] == 127                                   // 127.0.0.0/8 loopback
            || (b[0] == 169 && b[1] == 254)                  // 169.254.0.0/16 link-local
            || (b[0] == 172 && (b[1] & 0xF0) == 16)          // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // 192.0.0.0/24 IETF protocol assignments
            || (b[0] == 192 && b[1] == 168)                  // 192.168.0.0/16
            || (b[0] == 198 && (b[1] & 0xFE) == 18)          // 198.18.0.0/15 benchmarking
            || b[0] >= 224);                                 // multicast, reserved, broadcast
    }

    private static bool IsPublicV6(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return false;
        }

        var b = address.GetAddressBytes();

        if ((b[0] & 0xFE) == 0xFC)
        {
            return false; // fc00::/7 unique local
        }

        // Prefixes that embed an IPv4 address: judge by the embedded address.
        // ::/96 (IPv4-compatible) and 64:ff9b::/96 (NAT64)
        var embedsV4 = IsZero(b, 0, 12)
            || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && IsZero(b, 4, 12));
        if (embedsV4)
        {
            return IsPublicV4(b.AsSpan(12, 4));
        }

        return true;
    }

    private static bool IsZero(byte[] bytes, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (bytes[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}
