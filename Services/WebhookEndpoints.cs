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
        return mode == "subscribe" && FixedTimeEquals(token, options.Value.WhatsAppVerifyToken)
            ? Results.Text(challenge, "text/plain") : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    public static async Task<IResult> ReceiveAsync(HttpRequest request, IOptions<RePayOptions> options,
        WhatsAppOrchestrator orchestrator, ILoggerFactory loggerFactory)
    {
        using var bodyReader = new StreamReader(request.Body, Encoding.UTF8);
        var rawBody = await bodyReader.ReadToEndAsync();
        if (!IsValidSignature(request.Headers["X-Hub-Signature-256"], rawBody, options.Value.MetaAppSecret))
            return Results.Unauthorized();

        try { await orchestrator.HandleAsync(rawBody, request.HttpContext.RequestAborted); }
        catch (Exception ex) { loggerFactory.CreateLogger("Webhook").LogError(ex, "Webhook handling failed"); }
        return Results.Ok(); // Meta retries non-2xx responses.
    }

    private static bool IsValidSignature(string? header, string body, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header)) return false;
        var supplied = header.Replace("sha256=", "", StringComparison.OrdinalIgnoreCase);
        var actual = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
        return FixedTimeEquals(supplied, actual);
    }
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
