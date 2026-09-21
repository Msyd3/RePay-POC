using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RePay.WhatsAppPoc.Services;

public sealed class ConversationStore
{
    private readonly ConcurrentDictionary<string, int> _awaitingChoice = new();
    private readonly ConcurrentDictionary<string, int> _awaitingName = new();
    private readonly ConcurrentDictionary<string, RePayUser> _users = new();
    private readonly string _storagePath;
    private readonly object _storageLock = new();

    public ConversationStore(IHostEnvironment environment)
    {
        _storagePath = Path.Combine(environment.ContentRootPath, "App_Data", "users.json");
        if (!File.Exists(_storagePath)) return;

        try
        {
            var savedUsers = JsonSerializer.Deserialize<List<RePayUser>>(File.ReadAllText(_storagePath)) ?? [];
            foreach (var user in savedUsers) _users[user.Phone] = user;
        }
        catch (JsonException)
        {
            // A malformed local POC file must not stop webhook processing.
        }
    }

    public bool IsChoice(string phone) => _awaitingChoice.ContainsKey(phone);
    public void AwaitChoice(string phone) => _awaitingChoice[phone] = 1;
    public void CompleteChoice(string phone) => _awaitingChoice.TryRemove(phone, out _);

    public bool IsNewUser(string phone) => !_users.ContainsKey(phone) && !_awaitingName.ContainsKey(phone);
    public bool IsAwaitingName(string phone) => _awaitingName.ContainsKey(phone);
    public void BeginRegistration(string phone) => _awaitingName[phone] = 1;
    public void SaveUser(string phone, string name)
    {
        _users[phone] = new RePayUser(phone, name.Trim(), DateTimeOffset.UtcNow);
        _awaitingName.TryRemove(phone, out _);
        lock (_storageLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
            File.WriteAllText(_storagePath, JsonSerializer.Serialize(_users.Values.OrderBy(user => user.Phone)));
        }
    }
}

public sealed record RePayUser(string Phone, string Name, DateTimeOffset RegisteredAt);

file static class SaudiPhoneNumber
{
    private static readonly Regex Pattern = new("^9665\\d{8}$", RegexOptions.CultureInvariant);

    public static bool IsMatch(string phone) => Pattern.IsMatch(phone);
}

public sealed class WhatsAppOrchestrator(MetaWhatsAppClient whatsapp, AgentRouter agent, SafeBrowserAgent browser, ConversationStore store)
{
    public async Task HandleAsync(string rawPayload, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(rawPayload);
        foreach (var message in ExtractTextMessages(json.RootElement))
        {
            if (!SaudiPhoneNumber.IsMatch(message.Phone))
            {
                await whatsapp.SendTextAsync(message.Phone,
                    "عذرًا، خدمة RePay التجريبية متاحة حاليًا للأرقام السعودية فقط (+966 5xxxxxxxx).\n\n_RePay المالية_", ct);
                continue;
            }

            if (store.IsNewUser(message.Phone))
            {
                store.BeginRegistration(message.Phone);
                await whatsapp.SendTextAsync(message.Phone,
                    "أهلًا بك في RePay 👋\n\nأنا مساعدك لتجهيز مشترياتك بأمان حتى مرحلة المراجعة قبل الدفع.\n\nما الاسم الذي تحب أن أناديك به؟\n\n_RePay المالية_", ct);
                continue;
            }

            if (store.IsAwaitingName(message.Phone))
            {
                var name = message.Text.Trim();
                if (name.Length is < 2 or > 80)
                {
                    await whatsapp.SendTextAsync(message.Phone, "اكتب اسمك فقط، من حرفين إلى 80 حرفًا.\n\n_RePay المالية_", ct);
                    continue;
                }

                store.SaveUser(message.Phone, name);
                await whatsapp.SendTextAsync(message.Phone,
                    $"حياك الله يا {name} ✨\n\nتم إنشاء حسابك في RePay. اكتب لي ما الذي تريد البحث عنه وسأعرض لك الخيارات وأجهز الطلب حتى المراجعة قبل الدفع.\n\n_RePay المالية_", ct);
                continue;
            }

            if (store.IsChoice(message.Phone) && int.TryParse(message.Text.Trim(), out var choice) && choice is >= 1 and <= 3)
            {
                store.CompleteChoice(message.Phone);
                var checkout = await browser.PrepareCheckoutAsync(message.Text.Trim(), ct);
                await whatsapp.SendTextAsync(message.Phone, $"{checkout}\n\nتوقفت هنا قبل الدفع. راجع الملخص وأرسل تأكيدك لاحقًا في نسخة RePay المتكاملة.", ct);
                continue;
            }
            try
            {
                var reply = await agent.ReplyAsync(message.Text, ct);
                store.AwaitChoice(message.Phone);
                await whatsapp.SendTextAsync(message.Phone, reply, ct);
            }
            catch (Exception)
            {
                await whatsapp.SendTextAsync(message.Phone,
                    "مساعد RePay غير متاح مؤقتًا. تحقق من مفتاح Groq وحدود الاستخدام ثم أعد المحاولة.", ct);
            }
        }
    }

    private static IEnumerable<(string Phone, string Text)> ExtractTextMessages(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries)) yield break;
        foreach (var entry in entries.EnumerateArray())
        foreach (var change in entry.GetProperty("changes").EnumerateArray())
        {
            var value = change.GetProperty("value");
            if (!value.TryGetProperty("messages", out var messages)) continue;
            foreach (var message in messages.EnumerateArray())
                if (message.TryGetProperty("from", out var from) && message.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var body))
                    yield return (from.GetString()!, body.GetString()!);
        }
    }
}
