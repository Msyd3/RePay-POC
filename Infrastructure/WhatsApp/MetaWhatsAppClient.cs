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
        // Allowlisted diagnostics only: never log provider messages, raw bodies or credentials.
        string code = "unknown", subcode = "unknown", trace = "unknown", transient = "unknown";
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (error.RootElement.ValueKind == JsonValueKind.Object
                && error.RootElement.TryGetProperty("error", out var details)
                && details.ValueKind == JsonValueKind.Object)
            {
                if (details.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var n)) code = n.ToString();
                if (details.TryGetProperty("error_subcode", out var sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt64(out var sn)) subcode = sn.ToString();
                if (details.TryGetProperty("is_transient", out var t) && t.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    transient = t.GetBoolean() ? "true" : "false";
                if (details.TryGetProperty("fbtrace_id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var candidate = id.GetString();
                    if (candidate is { Length: > 0 and <= 128 } && candidate.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
                        trace = candidate;
                }
            }
        }
        catch (JsonException) { }
        throw new HttpRequestException($"Meta send failed: HTTP {(int)response.StatusCode}, code {code}, subcode {subcode}, transient {transient}, trace {trace}", null, response.StatusCode);
    }
}

