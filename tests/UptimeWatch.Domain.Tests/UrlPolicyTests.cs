namespace UptimeWatch.Domain.Tests;

public class UrlPolicyTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/p?x=1")]
    public void Accepts_public_http_urls(string url)
    {
        Assert.True(UrlPolicy.TryValidate(url, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("", ErrorCode.UrlRequired)]
    [InlineData(null, ErrorCode.UrlRequired)]
    [InlineData("example.com", ErrorCode.UrlNotAbsolute)]
    [InlineData("ftp://x.com", ErrorCode.UrlSchemeNotAllowed)]
    [InlineData("file:///etc/passwd", ErrorCode.UrlSchemeNotAllowed)]
    [InlineData("javascript:alert(1)", ErrorCode.UrlSchemeNotAllowed)]
    [InlineData("http://user:pass@example.com", ErrorCode.UrlHasCredentials)]
    [InlineData("http://localhost", ErrorCode.UrlLocalAddress)]
    [InlineData("http://127.0.0.1:8080", ErrorCode.UrlPrivateAddress)]
    [InlineData("http://[::1]/", ErrorCode.UrlPrivateAddress)]
    [InlineData("http://10.0.0.5", ErrorCode.UrlPrivateAddress)]
    [InlineData("http://localhost.", ErrorCode.UrlLocalAddress)]
    [InlineData("http://2130706433", ErrorCode.UrlPrivateAddress)]
    [InlineData("http://0x7f.0.0.1", ErrorCode.UrlPrivateAddress)]
    [InlineData("http://[::ffff:127.0.0.1]/", ErrorCode.UrlPrivateAddress)]
    [InlineData("http://169.254.169.254/latest/meta-data", ErrorCode.UrlPrivateAddress)]
    public void Rejects_invalid_or_unsafe_urls_with_a_specific_code(string? url, ErrorCode expected)
    {
        Assert.False(UrlPolicy.TryValidate(url, out var error));
        Assert.NotNull(error);
        Assert.Equal(expected, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Rejects_url_longer_than_2048()
    {
        const string prefix = "https://example.com/";
        var url = prefix + new string('a', 2049 - prefix.Length);
        Assert.Equal(2049, url.Length);
        Assert.False(UrlPolicy.TryValidate(url, out var error));
        Assert.Equal(ErrorCode.UrlTooLong, error!.Code);
        Assert.Equal([MonitorLimits.MaxUrlLength], error.Args);
    }

    [Fact]
    public void Rejects_string_of_2049_chars_even_if_not_a_url()
    {
        Assert.False(UrlPolicy.TryValidate(new string('a', 2049), out var error));
        Assert.Equal(ErrorCode.UrlTooLong, error!.Code);
    }

    [Fact]
    public void Accepts_url_of_exactly_2048()
    {
        const string prefix = "https://example.com/";
        var url = prefix + new string('a', 2048 - prefix.Length);
        Assert.True(UrlPolicy.TryValidate(url, out _));
    }
}
