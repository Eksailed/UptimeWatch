using System.Net;
using UptimeWatch.Infrastructure.Checks;

namespace UptimeWatch.Integration.Tests;

public class AddressGuardTests
{
    private sealed class StubDns(params string[] addresses) : IDnsResolver
    {
        public int Calls { get; private set; }

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());
        }
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public async Task Throws_when_host_resolves_to_non_public(string address)
    {
        var dns = new StubDns(address);

        await Assert.ThrowsAsync<BlockedAddressException>(() =>
            AddressGuard.ResolveAllowedAsync("example.com", dns, false, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_host_resolves_to_ipv4_mapped_private()
    {
        var dns = new StubDns("::ffff:10.0.0.1");

        await Assert.ThrowsAsync<BlockedAddressException>(() =>
            AddressGuard.ResolveAllowedAsync("example.com", dns, false, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_any_of_several_addresses_is_private()
    {
        var dns = new StubDns("8.8.8.8", "192.168.1.1");

        await Assert.ThrowsAsync<BlockedAddressException>(() =>
            AddressGuard.ResolveAllowedAsync("example.com", dns, false, CancellationToken.None));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task Throws_for_private_ip_literal_without_calling_resolver(string literal)
    {
        var dns = new StubDns("8.8.8.8");

        await Assert.ThrowsAsync<BlockedAddressException>(() =>
            AddressGuard.ResolveAllowedAsync(literal, dns, false, CancellationToken.None));
        Assert.Equal(0, dns.Calls);
    }

    [Fact]
    public async Task Returns_addresses_when_all_are_public()
    {
        var dns = new StubDns("93.184.216.34");

        var result = await AddressGuard.ResolveAllowedAsync("example.com", dns, false, CancellationToken.None);

        Assert.Equal([IPAddress.Parse("93.184.216.34")], result);
    }

    [Fact]
    public async Task Allows_private_when_allowPrivate_is_true()
    {
        var dns = new StubDns("127.0.0.1");

        var result = await AddressGuard.ResolveAllowedAsync("example.com", dns, true, CancellationToken.None);

        Assert.Equal([IPAddress.Loopback], result);
    }
}
