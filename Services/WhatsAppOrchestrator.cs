using System.Collections.Concurrent;
using System.Text.Json;

namespace RePay.WhatsAppPoc.Services;

public sealed class ConversationStore
{
    private readonly ConcurrentDictionary<string, int> _awaitingChoice = new();
    public bool IsChoice(string phone) => _awaitingChoice.ContainsKey(phone);
    public void AwaitChoice(string phone) => _awaitingChoice[phone] = 1;
    public void CompleteChoice(string phone) => _awaitingChoice.TryRemove(phone, out _);
}

public sealed class WhatsAppOrchestrator(MetaWhatsAppClient whatsapp, OpenAiAgentClient agent, SafeBrowserAgent browser, ConversationStore store)
{
    public async Task HandleAsync(string rawPayload, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(rawPayload);
        foreach (var message in ExtractTextMessages(json.RootElement))
        {
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
                    "مساعد RePay غير متاح مؤقتًا لأن رصيد OpenAI API يحتاج شحنًا. أعد المحاولة بعد إضافة رصيد للمشروع.", ct);
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
