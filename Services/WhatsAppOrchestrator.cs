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
            else if (state.AwaitingNameChange && Regex.IsMatch(message.Text.Trim(), @"^(إلغاء|الغاء|خلاص|لا تغيره|لا تغيّر اسمي)$"))
            {
                state.AwaitingNameChange = false;
                reply = "تمام، اسمك يبقى مثل ما هو.";
            }
            else if (ServiceConversation.IsNameChange(message.Text))
            {
                if (ServiceConversation.TryGetChangedName(message.Text, out var changedName))
                {
                    state.Name = changedName;
                    state.AwaitingNameChange = false;
                    reply = $"تم، حدّثت اسمك إلى {changedName}.";
                }
                else
                {
                    state.AwaitingNameChange = true;
                    reply = "أكيد، وش الاسم اللي تفضّله؟";
                }
            }
            else if (state.AwaitingNameChange && ServiceConversation.TryGetName(message.Text, out var changedName))
            {
                state.Name = changedName;
                state.AwaitingNameChange = false;
                reply = $"تم، حدّثت اسمك إلى {changedName}.";
            }
            else if (state.Name is null && ServiceConversation.TryGetName(message.Text, out var name))
            {
                state.Name = name;
                reply = $"تشرفنا يا {name}، وش حاب تعرف عن ري باي؟";
            }
            else
            {
                state.AwaitingNameChange = false;
                // Never pass the phone number to an AI provider. No browser or payment tools are registered.
                var context = JsonSerializer.Serialize(new { name = state.Name, recentConversation = state.Turns.TakeLast(8), currentMessage = message.Text });
                try { reply = await agent.ReplyAsync(context, ct); }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    reply = "تعذّر عليّ الرد الآن، جرّب ترسل سؤالك مرة ثانية.";
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
