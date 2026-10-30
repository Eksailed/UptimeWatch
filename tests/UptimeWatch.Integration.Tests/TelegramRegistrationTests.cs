using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UptimeWatch.Application;
using UptimeWatch.Infrastructure;
using UptimeWatch.Infrastructure.Notifications;

namespace UptimeWatch.Integration.Tests;

/// <summary>AddInfrastructure is the single place that picks the notification channel.</summary>
public sealed class TelegramRegistrationTests
{
    private static ServiceCollection Register(string? token)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=unused" };
        if (token is not null)
        {
            settings["Telegram:BotToken"] = token;
        }

        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }

    private static Type? ImplementationOf<T>(ServiceCollection services) =>
        services.Last(d => d.ServiceType == typeof(T)).ImplementationType;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_token_selects_NullNotifier(string? token)
    {
        var services = Register(token);

        Assert.Equal(typeof(NullNotifier), ImplementationOf<INotifier>(services));
        Assert.Equal(typeof(NullTelegramSender), ImplementationOf<ITelegramSender>(services));
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<TelegramOptions>().IsConfigured);
    }

    [Fact]
    public void Configured_token_selects_TelegramNotifier_and_real_sender()
    {
        var services = Register("123456789:AAE-test_token");

        Assert.Equal(typeof(TelegramNotifier), ImplementationOf<INotifier>(services));
        Assert.Null(ImplementationOf<ITelegramSender>(services));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<TelegramSender>(provider.GetRequiredService<ITelegramSender>());
        Assert.True(provider.GetRequiredService<TelegramOptions>().IsConfigured);
    }

    [Fact]
    public void Malformed_token_fails_at_startup_without_echoing_it()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Register("secret-without-colon"));

        Assert.Contains("Telegram:BotToken", ex.Message);
        Assert.DoesNotContain("secret-without-colon", ex.Message);
    }
}
