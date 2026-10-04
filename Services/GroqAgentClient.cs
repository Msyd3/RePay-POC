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


    public async Task<string> ReplyAsync(string userText, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.GroqApiKey);
        request.Content = JsonContent.Create(new
        {
            model = ServiceConversation.ChatModel,
            messages = new[]
            {
                new { role = "system", content = ServiceConversation.Instructions },
                new { role = "user", content = userText }
            },
            temperature = 0.3,
            max_completion_tokens = 1000
        });

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? "لم أتمكن من إعداد الرد الآن.";
    }
}
