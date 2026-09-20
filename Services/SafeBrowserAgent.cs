using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace RePay.WhatsAppPoc.Services;

public sealed class SafeBrowserAgent(HttpClient http, IOptions<RePayOptions> options)
{
    private readonly RePayOptions _options = options.Value;
    public async Task<string> PrepareCheckoutAsync(string productChoice, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.BrowserAgentBaseUrl))
            return "تم اختيار المنتج. اربط BROWSER_AGENT_BASE_URL لتشغيل المتصفح والوصول إلى صفحة تأكيد الطلب.";

        // The receiving browser service must expose only this guarded operation. There is intentionally no pay() operation.
        var url = $"{_options.BrowserAgentBaseUrl.TrimEnd('/')}/prepare-checkout";
        var response = await http.PostAsJsonAsync(url, new { choice = productChoice, stopBeforePayment = true }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
