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
        using var response = await http.SendAsync(request, ct);
        await EnsureMetaSuccessAsync(response, ct);
    }
    public async Task SendTextAsync(string to, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{_options.MetaGraphApiVersion}/{_options.MetaPhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.MetaAccessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to, type = "text", text = new { body = text } });
        using var response = await http.SendAsync(request, ct);
        await EnsureMetaSuccessAsync(response, ct);
    }
    private static async Task EnsureMetaSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        // Numeric provider diagnostics only: never log raw responses, tokens or message bodies.
        string code = "unknown", subcode = "unknown";
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (error.RootElement.TryGetProperty("error", out var details))
            {
                if (details.TryGetProperty("code", out var c) && c.TryGetInt64(out var n)) code = n.ToString();
                if (details.TryGetProperty("error_subcode", out var sc) && sc.TryGetInt64(out var sn)) subcode = sn.ToString();
            }
        }
        catch (JsonException) { }
        throw new HttpRequestException($"Meta send failed: HTTP {(int)response.StatusCode}, code {code}, subcode {subcode}", null, response.StatusCode);
    }
}

