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
    [InlineData("")]
    [InlineData(null)]
    [InlineData("example.com")]
    [InlineData("ftp://x.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://user:pass@example.com")]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]/")]
    [InlineData("http://10.0.0.5")]
    [InlineData("http://localhost.")]
    [InlineData("http://2130706433")]
    [InlineData("http://0x7f.0.0.1")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public void Rejects_invalid_or_unsafe_urls(string? url)
    {
        Assert.False(UrlPolicy.TryValidate(url, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Rejects_url_longer_than_2048()
    {
        const string prefix = "https://example.com/";
        var url = prefix + new string('a', 2049 - prefix.Length);
        Assert.Equal(2049, url.Length);
        Assert.False(UrlPolicy.TryValidate(url, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Rejects_string_of_2049_chars_even_if_not_a_url()
    {
        Assert.False(UrlPolicy.TryValidate(new string('a', 2049), out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Accepts_url_of_exactly_2048()
    {
        const string prefix = "https://example.com/";
        var url = prefix + new string('a', 2048 - prefix.Length);
        Assert.True(UrlPolicy.TryValidate(url, out _));
    }
}
