using System.Net;

namespace UptimeWatch.Domain.Tests;

public class IpAddressPolicyTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("100:0:0:1::1")] // just outside 100::/64
    [InlineData("2001:4860:4860::8888")]
    public void Public_addresses_are_allowed(string ip)
    {
        Assert.True(IpAddressPolicy.IsPublic(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b:1::1")]
    [InlineData("64:ff9b:1:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("2002::1")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("2002:808:808::1")]
    [InlineData("100::1")]
    [InlineData("100::ffff:ffff:ffff:ffff")]
    public void Non_public_addresses_are_blocked(string ip)
    {
        Assert.False(IpAddressPolicy.IsPublic(IPAddress.Parse(ip)));
    }
}
