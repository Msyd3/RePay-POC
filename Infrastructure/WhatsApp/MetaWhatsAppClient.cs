using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RePay.WhatsAppPoc.Services;

public sealed class MetaWhatsAppClient(HttpClient http, IOptions<RePayOptions> options)
{
    private readonly RePayOptions _options = options.Value;
    public string PhoneNumberId => _options.MetaPhoneNumberId;
    public async Task SendReviewAsync(string to, string text, string reviewId, string language, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{_options.MetaGraphApiVersion}/{_options.MetaPhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.MetaAccessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to, type = "interactive", interactive = new { type = "cta_url", body = new { text }, action = new { name = "cta_url", parameters = new { url = "https://repay.sa", display_text = language == "en" ? "Confirm operation" : "تأكيد العملية" } } } });
        // Materialize JSON so HTTP/1.1 uses Content-Length rather than chunked framing.
        await request.Content.LoadIntoBufferAsync(ct);
        using var response = await http.SendAsync(request, ct);
        await EnsureMetaSuccessAsync(response, ct);
    }
    public async Task SendTextAsync(string to, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{_options.MetaGraphApiVersion}/{_options.MetaPhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.MetaAccessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to, type = "text", text = new { body = text } });
        // Materialize JSON so HTTP/1.1 uses Content-Length rather than chunked framing.
        await request.Content.LoadIntoBufferAsync(ct);
        using var response = await http.SendAsync(request, ct);
        await EnsureMetaSuccessAsync(response, ct);
    }
    private static async Task EnsureMetaSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        // Allowlisted, bounded provider diagnostics only. Never log request bodies or credentials.
        string code = "unknown", subcode = "unknown", trace = "unknown", transient = "unknown";
        string type = "unknown", message = "unknown", details = "unknown";
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (error.RootElement.ValueKind == JsonValueKind.Object
                && error.RootElement.TryGetProperty("error", out var errorObject)
                && errorObject.ValueKind == JsonValueKind.Object)
            {
                if (errorObject.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var n)) code = n.ToString();
                if (errorObject.TryGetProperty("error_subcode", out var sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt64(out var sn)) subcode = sn.ToString();
                if (errorObject.TryGetProperty("is_transient", out var t) && t.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    transient = t.GetBoolean() ? "true" : "false";
                if (errorObject.TryGetProperty("fbtrace_id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var candidate = id.GetString();
                    if (candidate is { Length: > 0 and <= 128 } && candidate.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
                        trace = candidate;
                }
                type = ReadSafeDiagnostic(errorObject, "type");
                message = ReadSafeDiagnostic(errorObject, "message");
                if (errorObject.TryGetProperty("error_data", out var errorData)
                    && errorData.ValueKind == JsonValueKind.Object)
                    details = ReadSafeDiagnostic(errorData, "details");
            }
        }
        catch (JsonException) { }
        throw new HttpRequestException(
            $"Meta send failed: HTTP {(int)response.StatusCode}, code {code}, subcode {subcode}, transient {transient}, type {type}, message {message}, details {details}, trace {trace}",
            null, response.StatusCode);
    }

    private static string ReadSafeDiagnostic(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "unknown";
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text)) return "unknown";
        // Keep one-line, bounded diagnostics to prevent control characters or oversized log entries.
        var safe = new string(text.Where(ch => !char.IsControl(ch)).Take(256).ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }
}
