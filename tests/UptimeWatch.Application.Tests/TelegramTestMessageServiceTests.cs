using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace UptimeWatch.Application.Tests;

public sealed class TelegramTestMessageServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private readonly ITelegramSender _sender = Substitute.For<ITelegramSender>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
    private readonly TelegramTestMessageService _service;

    public TelegramTestMessageServiceTests()
    {
        _service = new TelegramTestMessageService(_sender, _time);
    }

    [Fact]
    public async Task Second_attempt_within_30_seconds_is_rejected_without_calling_sender_and_passes_after()
    {
        Assert.Null(await _service.SendTestAsync("alice", "1", Ct));
        await _sender.Received(1).SendAsync("1", TelegramTestMessageService.TestText, Ct);

        _time.Advance(TimeSpan.FromSeconds(10));
        var retry = await _service.SendTestAsync("alice", "1", Ct);
        Assert.Equal(TimeSpan.FromSeconds(20), retry);
        await _sender.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Null(await _service.SendTestAsync("alice", "1", Ct));
        await _sender.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cooldown_is_per_user()
    {
        Assert.Null(await _service.SendTestAsync("alice", "1", Ct));
        Assert.Null(await _service.SendTestAsync("bob", "2", Ct));
        Assert.NotNull(await _service.SendTestAsync("alice", "1", Ct));
        Assert.NotNull(await _service.SendTestAsync("bob", "2", Ct));
        await _sender.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cooldown_starts_even_when_the_send_fails_and_the_exception_propagates()
    {
        _sender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SendTestAsync("alice", "1", Ct));

        Assert.NotNull(await _service.SendTestAsync("alice", "1", Ct));
        await _sender.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
