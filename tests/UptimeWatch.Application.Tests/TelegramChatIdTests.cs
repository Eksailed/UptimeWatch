namespace UptimeWatch.Application.Tests;

public sealed class TelegramChatIdTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  123  ", "123")]
    public void Normalize_trims_and_turns_blank_into_null(string? input, string? expected) =>
        Assert.Equal(expected, TelegramChatId.Normalize(input));

    [Theory]
    [InlineData("123456789")]
    [InlineData("-1001234567890")]
    [InlineData("@my_channel")]
    [InlineData("@A1")]
    public void Valid_values_are_accepted(string value)
    {
        Assert.True(TelegramChatId.TryValidate(value, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Cleared_value_is_valid() => Assert.True(TelegramChatId.TryValidate(null, out _));

    [Theory]
    [InlineData("abc")]
    [InlineData("12 34")]
    [InlineData("--123")]
    [InlineData("-")]
    [InlineData("1-2")]
    [InlineData("@")]
    [InlineData("@a")]
    [InlineData("@1abc")]
    [InlineData("@bad-name")]
    [InlineData("123\n456")]
    [InlineData("<b>1</b>")]
    public void Invalid_values_are_rejected_with_a_message(string value)
    {
        Assert.False(TelegramChatId.TryValidate(value, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Values_longer_than_64_characters_are_rejected()
    {
        Assert.True(TelegramChatId.TryValidate(new string('1', 64), out _));
        Assert.False(TelegramChatId.TryValidate(new string('1', 65), out _));
    }
}
