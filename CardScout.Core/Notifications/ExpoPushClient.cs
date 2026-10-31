using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardScout.Core.Data;
using Microsoft.Extensions.Logging;

namespace CardScout.Core.Notifications;

public record PushMessage(string Title, string Body, IReadOnlyDictionary<string, string>? Data = null);

// Sends notifications to a user's devices through Expo's push service
// (https://docs.expo.dev/push-notifications/sending-notifications/). No credentials are
// needed on our side; Expo holds the APNs/FCM keys for the app's EAS project.
public class ExpoPushClient
{
    private const string Endpoint = "https://exp.host/--/api/v2/push/send";

    private readonly HttpClient _http;
    private readonly PushTokenRepository _tokens;
    private readonly ILogger<ExpoPushClient> _logger;

    public ExpoPushClient(HttpClient http, PushTokenRepository tokens, ILogger<ExpoPushClient> logger)
    {
        _http = http;
        _tokens = tokens;
        _logger = logger;
    }

    // Returns how many devices accepted the message
    public async Task<int> SendAsync(long userId, PushMessage message, CancellationToken ct = default)
    {
        var tokens = await _tokens.GetTokensAsync(userId);
        if (tokens.Count == 0) return 0;

        var payload = tokens.Select(t => new ExpoMessage(t, message.Title, message.Body, message.Data, "default")).ToList();

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(Endpoint, payload, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Expo push request failed for user {UserId}", userId);
            return 0;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Expo push returned {Status}: {Body}", (int)response.StatusCode, body);
            return 0;
        }

        // One ticket per message, in order. A DeviceNotRegistered ticket means the app was
        // uninstalled or the token rotated, so stop sending to it.
        var sent = 0;
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("data", out var tickets) && tickets.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var ticket in tickets.EnumerateArray())
            {
                var token = i < tokens.Count ? tokens[i] : null;
                i++;
                if (ticket.TryGetProperty("status", out var status) && status.GetString() == "ok")
                {
                    sent++;
                    continue;
                }
                var error = ticket.TryGetProperty("details", out var details) && details.TryGetProperty("error", out var e)
                    ? e.GetString()
                    : null;
                _logger.LogWarning("Expo push ticket error for user {UserId}: {Error} {Message}", userId, error,
                    ticket.TryGetProperty("message", out var m) ? m.GetString() : null);
                if (error == "DeviceNotRegistered" && token != null)
                    await _tokens.DeleteAsync(userId, token);
            }
        }
        return sent;
    }

    private record ExpoMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Data,
        [property: JsonPropertyName("sound")] string Sound);
}
