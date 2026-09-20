using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace RePay.WhatsAppPoc.Services;

public sealed class MetaWhatsAppClient(HttpClient http, IOptions<RePayOptions> options)
{
    private readonly RePayOptions _options = options.Value;
    public async Task SendTextAsync(string to, string text, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{_options.MetaGraphApiVersion}/{_options.MetaPhoneNumberId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.MetaAccessToken);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", to, type = "text", text = new { body = text } });
        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
