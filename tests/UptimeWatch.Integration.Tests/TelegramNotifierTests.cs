using System.Globalization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using UptimeWatch.Application;
using UptimeWatch.Domain;
using UptimeWatch.Infrastructure.Notifications;

namespace UptimeWatch.Integration.Tests;

public sealed class TelegramNotifierTests
{
    private const string Owner = "alice";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTime T = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly ITelegramSender _sender = Substitute.For<ITelegramSender>();
    private readonly IUserProfileStore _profiles = Substitute.For<IUserProfileStore>();
    private readonly TelegramNotifier _notifier;

    public TelegramNotifierTests()
    {
        _notifier = new TelegramNotifier(_sender, _profiles);
    }

    private void HasChatId(string? chatId) =>
        _profiles.GetTelegramChatIdAsync(Owner, Arg.Any<CancellationToken>()).Returns(chatId);

    /// <summary>Opens an incident through the real domain logic (3 failures), optionally closing it after <paramref name="downtime"/>.</summary>
    private static (SiteMonitor Monitor, Incident Incident) Incident(string url, string? error, TimeSpan? downtime = null)
    {
        var monitor = SiteMonitor.Create(Owner, url, 60, 200, T);
        for (var i = 0; i < MonitorLimits.FailureThreshold; i++)
        {
            monitor.ApplyResult(CheckResult.Create(monitor.Id, T, false, null, 10, error), T);
        }

        var incident = monitor.OpenIncident!;
        if (downtime is { } d)
        {
            monitor.ApplyResult(CheckResult.Create(monitor.Id, T + d, true, 200, 10, null), T + d);
            Assert.NotNull(incident.EndedAt);
        }

        return (monitor, incident);
    }

    [Fact]
    public async Task Notifier_skips_silently_when_user_has_no_chat_id()
    {
        var (monitor, incident) = Incident("https://example.com", "boom", TimeSpan.FromMinutes(5));

        foreach (var chatId in new string?[] { null, "", "   " })
        {
            HasChatId(chatId);
            await _notifier.NotifyDownAsync(monitor, incident, Ct);
            await _notifier.NotifyRecoveredAsync(monitor, incident, Ct);
        }

        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }

    [Fact]
    public async Task Alerts_stay_russian_when_the_current_ui_culture_is_english()
    {
        HasChatId("12345");
        var (monitor, incident) = Incident("https://example.com", "boom", TimeSpan.FromMinutes(5));
        var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("en");

            await _notifier.NotifyDownAsync(monitor, incident, Ct);
            await _notifier.NotifyRecoveredAsync(monitor, incident, Ct);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
        }

        var texts = _sender.ReceivedCalls().Select(c => (string)c.GetArguments()[1]!).ToList();
        Assert.Equal(2, texts.Count);
        Assert.Contains("Сайт недоступен", texts[0]);
        Assert.Contains("Ошибка: boom", texts[0]);
        Assert.Contains("Сайт снова доступен", texts[1]);
        Assert.Contains("Время недоступности: 5 мин", texts[1]);
    }

    [Fact]
    public async Task Down_message_contains_url_and_last_error()
    {
        HasChatId("12345");
        var (monitor, incident) = Incident("https://example.com/health?a=1&b=<2>", "Connection refused");

        await _notifier.NotifyDownAsync(monitor, incident, Ct);

        var text = (string)_sender.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.Contains("https://example.com/health?a=1&b=<2>", text);
        Assert.Contains("Connection refused", text);
        await _sender.Received(1).SendAsync("12345", text, Ct);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Down_message_says_the_error_is_unknown_when_there_is_no_last_error(string? error)
    {
        HasChatId("12345");
        var (monitor, incident) = Incident("https://example.com", error);

        await _notifier.NotifyDownAsync(monitor, incident, Ct);

        var text = (string)_sender.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.Contains("https://example.com", text);
        Assert.EndsWith("Ошибка: неизвестна", text);
    }

    [Fact]
    public async Task Recovered_message_contains_url_and_downtime_duration()
    {
        HasChatId("12345");
        var (monitor, incident) = Incident("https://example.com", "boom", TimeSpan.FromMinutes(65) + TimeSpan.FromSeconds(30));

        await _notifier.NotifyRecoveredAsync(monitor, incident, Ct);

        var text = (string)_sender.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.Contains("https://example.com", text);
        Assert.Contains("1 ч 5 мин", text);
    }

    [Fact]
    public async Task Notifier_propagates_sender_exception()
    {
        HasChatId("12345");
        _sender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new TelegramApiException("Telegram API returned HTTP 500.", 500));
        var (monitor, incident) = Incident("https://example.com", "boom", TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<TelegramApiException>(() => _notifier.NotifyDownAsync(monitor, incident, Ct));
        await Assert.ThrowsAsync<TelegramApiException>(() => _notifier.NotifyRecoveredAsync(monitor, incident, Ct));
    }

    [Fact]
    public async Task Long_error_is_truncated_to_telegram_limit_and_url_is_kept()
    {
        HasChatId("12345");
        var url = "https://example.com/" + new string('p', 1500);
        // Surrogate pairs: the cut must not split one.
        var (monitor, incident) = Incident(url, string.Concat(Enumerable.Repeat("😀", 5000)));

        await _notifier.NotifyDownAsync(monitor, incident, Ct);

        var text = (string)_sender.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.True(text.Length <= TelegramNotifier.MaxMessageLength, $"length {text.Length}");
        Assert.Contains(url, text);
        Assert.EndsWith("…", text);
        Assert.False(char.IsHighSurrogate(text[^2]));
    }
}
