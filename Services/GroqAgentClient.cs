using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RePay.WhatsAppPoc.Services;

public sealed class GroqAgentClient(HttpClient http, IOptions<RePayOptions> options) : IAgentProvider
{
    private readonly RePayOptions _options = options.Value;
    public string Name => "Groq";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.GroqApiKey);
    private const string Instructions = "You are RePay's shopping assistant. Reply in Arabic unless the user writes another language. Search and compare products when the selected model supports it. Never request or accept card data, passwords, OTPs, or payment details. Never claim a purchase completed. Give short, WhatsApp-friendly options numbered 1 to 3. Once the customer selects an option, say you are preparing the basket and checkout summary. The system, not you, will stop before payment.";

    public async Task<string> ReplyAsync(string userText, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.GroqApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.GroqModel,
            messages = new[]
            {
                new { role = "system", content = Instructions },
                new { role = "user", content = userText }
            },
            temperature = 0.3
        });

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? "لم أتمكن من إعداد الرد الآن.";
    }
}
