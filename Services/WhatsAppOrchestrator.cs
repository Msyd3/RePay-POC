using System.Text.Json;
using System.Text.RegularExpressions;

namespace RePay.WhatsAppPoc.Services;

public sealed class WhatsAppOrchestrator(MetaWhatsAppClient whatsapp, AgentRouter agent, ConversationStore store)
{
    public async Task HandleAsync(string rawPayload, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(rawPayload);
        foreach (var message in ExtractTextMessages(json.RootElement))
        {
            if (!Regex.IsMatch(message.Phone, @"^9665[0-9]{8}$"))
            {
                await whatsapp.SendTextAsync(message.Phone, "خدمة ري باي متاحة حاليًا للأرقام السعودية فقط.", ct);
                continue;
            }
            await using var session = await store.OpenSessionAsync(message.Phone, ct);
            var state = session.State;
            if (state.ProcessedMessages.Contains(message.Id)) continue;
            string reply;
            if (!state.Welcomed && state.Name is null)
            {
                reply = ServiceConversation.Welcome;
                state.Welcomed = true;
            }
            else if (state.Name is null && ServiceConversation.TryGetName(message.Text, out var name))
            {
                state.Name = name;
                reply = $"تشرفنا يا {name} 🤍\n\nحفظت اسمك، وما راح أطلبه منك مرة ثانية. خدمات ري باي بتتاح تدريجيًا للمستخدمين، وحاليًا أقدر أعرّفك عليها وأجاوب عن أسئلتك. وش حاب تعرف؟";
            }
            else
            {
                // Never pass the phone number to an AI provider. No browser or payment tools are registered.
                var context = JsonSerializer.Serialize(new { name = state.Name, recentConversation = state.Turns.TakeLast(8), currentMessage = message.Text });
                try { reply = await agent.ReplyAsync(context, ct); }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    reply = "ري باي يهدف لمساعدتك في التصفح والتحويل والدفع بأمان، والخدمات بتتاح تدريجيًا للمستخدمين. حاليًا نعرّفك بالخدمات فقط، وما عندنا موعد إطلاق محدد نعلنه.";
                }
            }
            // Commit identity before delivery so a send failure never loses a saved name.
            await session.SaveAsync(ct);
            await whatsapp.SendTextAsync(message.Phone, reply, ct);
            state.Turns.Add(new ChatTurn("user", message.Text));
            state.Turns.Add(new ChatTurn("assistant", reply));
            state.Turns = state.Turns.TakeLast(8).ToList();
            state.ProcessedMessages.Add(message.Id);
            state.ProcessedMessages = state.ProcessedMessages.TakeLast(100).ToList();
            await session.SaveAsync(ct);
        }
    }

    private static IEnumerable<(string Id, string Phone, string Text)> ExtractTextMessages(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries)) yield break;
        foreach (var entry in entries.EnumerateArray())
        foreach (var change in entry.GetProperty("changes").EnumerateArray())
        {
            var value = change.GetProperty("value");
            if (!value.TryGetProperty("messages", out var messages)) continue;
            foreach (var message in messages.EnumerateArray())
                if (message.TryGetProperty("id", out var id) && message.TryGetProperty("from", out var from) && message.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var body))
                    yield return (id.GetString()!, from.GetString()!, body.GetString()!);
        }
    }
}
