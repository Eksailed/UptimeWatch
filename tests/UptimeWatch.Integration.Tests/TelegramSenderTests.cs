using System.Net;
using System.Text;
using System.Text.Json;
using UptimeWatch.Infrastructure.Notifications;

namespace UptimeWatch.Integration.Tests;

public sealed class TelegramSenderTests
{
    private const string Token = "123456789:AAE-test_token-0123456789";

    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request, Body);
        }
    }

    private static TelegramSender CreateSender(StubHandler handler, string token = Token) =>
        new(new HttpClient(handler) { BaseAddress = TelegramSender.ApiBaseAddress }, new TelegramOptions { BotToken = token });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sender_posts_chat_id_and_text_to_bot_endpoint()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"ok":true,"result":{}}"""));

        await CreateSender(handler).SendAsync("-1001234567890", "привет <b>мир</b> & co", Ct);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"https://api.telegram.org/bot{Token}/sendMessage", handler.Request.RequestUri!.ToString());
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("-1001234567890", body.RootElement.GetProperty("chat_id").GetString());
        Assert.Equal("привет <b>мир</b> & co", body.RootElement.GetProperty("text").GetString());
        // Plain text only: no parse_mode, so nothing in the text can be interpreted as markup.
        Assert.False(body.RootElement.TryGetProperty("parse_mode", out _));
    }

    [Fact]
    public async Task Sender_throws_on_non_success_status()
    {
        var handler = new StubHandler((_, _) =>
            Json(HttpStatusCode.BadRequest, """{"ok":false,"error_code":400,"description":"Bad Request: chat not found"}"""));

        var ex = await Assert.ThrowsAsync<TelegramApiException>(() => CreateSender(handler).SendAsync("1", "x", Ct));

        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("400", ex.Message);
        Assert.Contains("chat not found", ex.Message);
    }

    [Fact]
    public async Task Sender_throws_on_non_json_error_body_with_status_only()
    {
        var handler = new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>bad gateway</html>") });

        var ex = await Assert.ThrowsAsync<TelegramApiException>(() => CreateSender(handler).SendAsync("1", "x", Ct));

        Assert.Equal(502, ex.StatusCode);
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task Exception_messages_never_contain_the_token_or_url()
    {
        var failures = new Func<HttpRequestMessage, string?, HttpResponseMessage>[]
        {
            // Telegram echoing the token back in the description.
            (_, _) => Json(HttpStatusCode.Unauthorized, $$"""{"ok":false,"description":"Unauthorized {{Token}} bot{{Token}}/sendMessage"}"""),
            (_, _) => Json(HttpStatusCode.NotFound, """{"ok":false,"description":"Not Found"}"""),
            // Network failure whose message carries the request URL.
            (request, _) => throw new HttpRequestException($"Failed to call {request.RequestUri}")
        };

        foreach (var respond in failures)
        {
            var ex = await Assert.ThrowsAsync<TelegramApiException>(() => CreateSender(new StubHandler(respond)).SendAsync("1", "x", Ct));

            foreach (var text in new[] { ex.Message, ex.ToString().Split("--->", 2)[0] })
            {
                Assert.DoesNotContain(Token, text);
                Assert.DoesNotContain("AAE-test_token", text);
                Assert.DoesNotContain("api.telegram.org", text);
            }
        }
    }

    [Fact]
    public async Task Timeout_is_reported_as_telegram_exception_but_caller_cancellation_is_not()
    {
        var timeoutHandler = new StubHandler((_, _) => throw new TaskCanceledException("timeout with " + Token));
        var timeout = await Assert.ThrowsAsync<TelegramApiException>(() => CreateSender(timeoutHandler).SendAsync("1", "x", Ct));
        Assert.DoesNotContain(Token, timeout.Message);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateSender(handler).SendAsync("1", "x", cts.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("123:abc/../../evil")]
    [InlineData("123:abc def")]
    public void Sender_rejects_malformed_token_without_echoing_it(string token)
    {
        var ex = Assert.Throws<ArgumentException>(() => CreateSender(new StubHandler((_, _) => Json(HttpStatusCode.OK, "{}")), token));

        if (token.Length > 0)
        {
            Assert.DoesNotContain(token, ex.Message);
        }
    }

    [Fact]
    public void Options_ToString_does_not_reveal_the_token()
    {
        Assert.DoesNotContain(Token, new TelegramOptions { BotToken = Token }.ToString());
    }
}
