using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace RePay.WhatsAppPoc.Services;

public sealed class GeminiAgentClient(HttpClient http, IOptions<RePayOptions> options) : IAgentProvider
{
    private readonly RePayOptions _options = options.Value;
    public string Name => "Gemini";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.GeminiApiKey);
    private const string Instructions = "You are RePay's shopping assistant. Reply in Arabic unless the user writes another language. Never request card data, passwords, OTPs, or payment details. Never claim a purchase completed. Give short options numbered 1 to 3. The system stops before payment.";

    public async Task<string> ReplyAsync(string userText, CancellationToken ct)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_options.GeminiModel}:generateContent?key={Uri.EscapeDataString(_options.GeminiApiKey)}";
        var payload = new
        {
            systemInstruction = new { parts = new[] { new { text = Instructions } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userText } } } },
            generationConfig = new { temperature = 0.3 }
        };
        var response = await http.PostAsJsonAsync(url, payload, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()
            ?? "لم أتمكن من إعداد الرد الآن.";
    }
}
