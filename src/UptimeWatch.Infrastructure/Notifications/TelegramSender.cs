using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using UptimeWatch.Application;

namespace UptimeWatch.Infrastructure.Notifications;

/// <summary>
/// Calls the Telegram Bot API. The request URL contains the bot token, so this class deliberately works with a
/// plain <see cref="HttpClient"/> (no IHttpClientFactory, hence no request-URL logging) and never puts the URL
/// into an exception.
/// </summary>
public sealed class TelegramSender : ITelegramSender, IDisposable
{
    /// <summary>Fixed on purpose: the destination host is not configurable in production.</summary>
    public static readonly Uri ApiBaseAddress = new("https://api.telegram.org/");

    private const int MaxDescriptionLength = 200;

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly bool _ownsClient;

    /// <param name="http">Client whose BaseAddress is <see cref="ApiBaseAddress"/> (tests pass a stub handler).</param>
    public TelegramSender(HttpClient http, TelegramOptions options)
        : this(http, options, ownsClient: false)
    {
    }

    private TelegramSender(HttpClient http, TelegramOptions options, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (!TelegramOptions.IsValidToken(options.BotToken))
        {
            // Deliberately without the value.
            throw new ArgumentException("Telegram bot token has an unexpected format.", nameof(options));
        }

        _http = http;
        _token = options.BotToken;
        _ownsClient = ownsClient;
    }

    /// <summary>Production sender: its own client for <see cref="ApiBaseAddress"/>, disposed together with the sender.</summary>
    public static TelegramSender Create(TelegramOptions options)
    {
        var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = ApiBaseAddress,
            Timeout = TimeSpan.FromSeconds(15)
        };
        try
        {
            return new TelegramSender(http, options, ownsClient: true);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    public async Task SendAsync(string chatId, string text, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            // "./" matters: the token contains a colon, so "bot123:AA.." alone would be parsed as an absolute URI with a scheme.
            var endpoint = new Uri(_http.BaseAddress ?? ApiBaseAddress, $"./bot{_token}/sendMessage");
            using var response = await _http.PostAsJsonAsync(endpoint, new SendMessageRequest(chatId, text), ct);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var description = await ReadDescriptionAsync(response, ct);
                throw new TelegramApiException(
                    description is null
                        ? $"Telegram API returned HTTP {status}."
                        : $"Telegram API returned HTTP {status}: {description}",
                    status);
            }
        }
        catch (HttpRequestException ex)
        {
            throw new TelegramApiException($"Telegram request failed: {ex.GetType().Name}.", null, ex);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TelegramApiException("Telegram request timed out.", null, ex);
        }
    }

    private async Task<string?> ReadDescriptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(ct);
            var description = body?.Description?.Trim();
            if (string.IsNullOrEmpty(description))
            {
                return null;
            }

            description = description.Replace(_token, "***", StringComparison.Ordinal);
            return description.Length <= MaxDescriptionLength ? description : description[..MaxDescriptionLength] + "…";
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record SendMessageRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text);

    private sealed record ErrorResponse([property: JsonPropertyName("description")] string? Description);
}
