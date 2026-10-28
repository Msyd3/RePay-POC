using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RePay.WhatsAppPoc.Services;

public static class WebhookEndpoints
{
    public static IResult VerifyAsync(HttpRequest request, IOptions<RePayOptions> options)
    {
        var mode = request.Query["hub.mode"].ToString();
        var token = request.Query["hub.verify_token"].ToString();
        var challenge = request.Query["hub.challenge"].ToString();
        return !string.IsNullOrWhiteSpace(options.Value.WhatsAppVerifyToken) && mode == "subscribe" && FixedTimeEquals(token, options.Value.WhatsAppVerifyToken)
            ? Results.Text(challenge, "text/plain") : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    public static async Task<IResult> ReceiveAsync(HttpRequest request, IOptions<RePayOptions> options,
        WhatsAppOrchestrator orchestrator, ILoggerFactory loggerFactory, IHostApplicationLifetime lifetime)
    {
        using var bodyReader = new StreamReader(request.Body, Encoding.UTF8);
        var rawBody = await bodyReader.ReadToEndAsync();
        if (!IsValidSignature(request.Headers["X-Hub-Signature-256"], rawBody, options.Value.MetaAppSecret))
            return Results.Unauthorized();

        var logger = loggerFactory.CreateLogger("Webhook");
        LogRoutingMetadata(rawBody, options.Value.MetaPhoneNumberId, logger);

        // Finish sending and recording the reply even if Meta disconnects while waiting.
        // Otherwise a successful send can be retried because its delivery checkpoint was cancelled.
        using var processing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
        processing.CancelAfter(TimeSpan.FromSeconds(60));
        try { await orchestrator.HandleAsync(rawBody, processing.Token); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook handling failed");
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return Results.Ok(); // Meta retries non-2xx responses.
    }

    private static void LogRoutingMetadata(string rawBody, string configuredPhoneNumberId, ILogger logger)
    {
        try
        {
            using var json = JsonDocument.Parse(rawBody);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("entry", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
                return;

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var wabaId = ReadString(entry, "id") ?? "unknown";
                if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (change.ValueKind != JsonValueKind.Object
                        || !change.TryGetProperty("value", out var value)
                        || value.ValueKind != JsonValueKind.Object
                        || !value.TryGetProperty("metadata", out var metadata)
                        || metadata.ValueKind != JsonValueKind.Object)
                        continue;

                    var phoneNumberId = ReadString(metadata, "phone_number_id") ?? "unknown";
                    var displayPhoneNumber = ReadString(metadata, "display_phone_number") ?? "unknown";
                    var field = ReadString(change, "field") ?? "unknown";
                    var matchesConfiguredPhoneNumberId =
                        !string.IsNullOrWhiteSpace(configuredPhoneNumberId)
                        && string.Equals(phoneNumberId, configuredPhoneNumberId, StringComparison.Ordinal);

                    // Log routing metadata only. Never log raw webhook bodies, customer numbers or message text.
                    logger.LogInformation(
                        "WhatsApp webhook routing metadata: WabaId={WabaId}, PhoneNumberId={PhoneNumberId}, DisplayPhoneNumber={DisplayPhoneNumber}, Field={Field}, MatchesConfiguredPhoneNumberId={MatchesConfiguredPhoneNumberId}",
                        wabaId, phoneNumberId, displayPhoneNumber, field, matchesConfiguredPhoneNumberId);
                }
            }
        }
        catch (JsonException)
        {
            // The orchestrator retains responsibility for malformed payload handling.
            logger.LogWarning("Could not parse signed WhatsApp webhook routing metadata");
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsValidSignature(string? header, string body, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header)) return false;
        if (!header.StartsWith("sha256=", StringComparison.Ordinal) || header.Length != 71) return false;
        var supplied = header[7..];
        var actual = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        return FixedTimeEquals(supplied, actual);
    }
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
