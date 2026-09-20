using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RePay.WhatsAppPoc.Services;

public sealed class OpenAiAgentClient(HttpClient http, IOptions<RePayOptions> options)
{
    private readonly RePayOptions _options = options.Value;
    private const string Instructions = "You are RePay's shopping assistant. Reply in Arabic unless the user writes another language. You may search and compare products. Never request or accept card data, passwords, OTPs, or payment details. Never claim a purchase completed. Give short WhatsApp-friendly options numbered 1 to 3. Once the customer selects an option, say you are preparing the basket and checkout summary. The system, not you, will stop before payment.";

    public async Task<string> ReplyAsync(string userText, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);
        request.Content = JsonContent.Create(new { model = _options.OpenAiModel, instructions = Instructions, input = userText,
            tools = new object[] { new { type = "web_search" } } });
        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.TryGetProperty("output_text", out var output) ? output.GetString() ?? "لم أتمكن من إعداد الرد الآن." : "لم أتمكن من إعداد الرد الآن.";
    }
}
