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
                await whatsapp.SendTextAsync(message.Phone, "خدمة ري باي متاحة حاليًا للأرقام السعودية فقط", ct);
                continue;
            }
            await using var session = await store.OpenSessionAsync(message.Phone, ct);
            var state = session.State;
            if (state.ProcessedMessages.Contains(message.Id)) continue;
            string reply;
            var retry = state.PendingMessageId == message.Id && state.PendingReply is not null;
            if (!retry) { ConversationLifecycle.Expire(state, DateTimeOffset.UtcNow); state.PendingFollowUp = null; state.PendingReplyIndex = 0; }
            if (retry)
                reply = state.PendingReply!;
            else if (NameConversation.TryReply(state, message.Text, out var nameReply))
                reply = nameReply;
            else if (!state.Welcomed && state.Name is null)
            {
                reply = ServiceConversation.Welcome;
                state.Welcomed = true;
            }
            else if (state.Name is null && ServiceConversation.TryGetName(message.Text, out var name))
            {
                if (NameConversation.TryFullName(name, out var fullName))
                {
                    state.Name = fullName;
                    reply = $"تشرفنا يا {fullName}، وش حاب نجرب؟";
                }
                else
                {
                    state.AwaitingNameChange = true;
                    reply = "ممكن اسمك الثنائي؟ الاسم الأول واسم العائلة";
                }
            }
            else if (state.Name is not null && !NameConversation.TryFullName(state.Name, out _))
            {
                state.AwaitingNameChange = true;
                reply = "عشان نحدّث بياناتك، ممكن اسمك الثنائي؟ الاسم الأول واسم العائلة";
            }
            else if (DemoConversation.TryReply(state, message.Text, out var demoReply))
            {
                state.Welcomed = true;
                reply = demoReply;
            }
            else
            {
                state.AwaitingNameChange = false;
                // Never pass the phone number to an AI provider. No browser or payment tools are registered.
                var context = JsonSerializer.Serialize(new { name = state.Name, recentConversation = state.Turns.TakeLast(8).Select(t => new ChatTurn(t.Role, RedactPhone(t.Text))), currentMessage = RedactPhone(message.Text) });
                try { reply = await agent.ReplyAsync(context, ct); }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    reply = "تعذّر عليّ الرد الآن، جرّب ترسل سؤالك مرة ثانية.";
                }
            }
            reply = ConversationLifecycle.Format(reply);
            if (state.PendingFollowUp is not null) state.PendingFollowUp = ConversationLifecycle.Format(state.PendingFollowUp);
            // Persist the generated reply and demo transition, so retries do not advance the example twice.
            state.PendingMessageId = message.Id;
            state.PendingReply = reply;
            await session.SaveAsync(ct);
            if (state.PendingReplyIndex == 0)
            {
                await whatsapp.SendTextAsync(message.Phone, reply, ct);
                state.PendingReplyIndex = 1;
                await session.SaveAsync(ct);
            }
            if (state.PendingFollowUp is not null && state.PendingReplyIndex == 1)
            {
                await whatsapp.SendTextAsync(message.Phone, state.PendingFollowUp, ct);
                state.PendingReplyIndex = 2;
                await session.SaveAsync(ct);
            }
            var now = DateTimeOffset.UtcNow;
            if (state.LastMessageAt is null || now - state.LastMessageAt >= ConversationLifecycle.IdleTimeout) state.ConversationCount++;
            state.LastMessageAt = now;
            state.MessageCount++;
            state.PendingMessageId = null;
            state.PendingReply = null;
            state.Turns.Add(new ChatTurn("user", RedactPhone(message.Text)));
            state.Turns.Add(new ChatTurn("assistant", RedactPhone(reply)));
            if (state.PendingFollowUp is not null) state.Turns.Add(new ChatTurn("assistant", state.PendingFollowUp));
            state.PendingFollowUp = null;
            state.PendingReplyIndex = 0;
            state.Turns = state.Turns.TakeLast(8).ToList();
            state.ProcessedMessages.Add(message.Id);
            state.ProcessedMessages = state.ProcessedMessages.TakeLast(100).ToList();
            await session.SaveAsync(ct);
        }
    }

    private static string RedactPhone(string text) => Regex.Replace(text, @"[+＋]?[0-9٠-٩][0-9٠-٩ ()-]{7,}[0-9٠-٩]", "[رقم جوال]");

    private static IEnumerable<(string Id, string Phone, string Text)> ExtractTextMessages(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries)) yield break;
        foreach (var entry in entries.EnumerateArray())
        foreach (var change in entry.GetProperty("changes").EnumerateArray())
        {
            var value = change.GetProperty("value");
            if (!value.TryGetProperty("messages", out var messages)) continue;
            foreach (var message in messages.EnumerateArray())
            {
                if (!message.TryGetProperty("id", out var id) || !message.TryGetProperty("from", out var from)) continue;
                if (message.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var body))
                    yield return (id.GetString()!, from.GetString()!, body.GetString()!);
                else if (message.TryGetProperty("contacts", out var contacts) && contacts.GetArrayLength() > 0)
                {
                    var contact = contacts[0];
                    var name = contact.TryGetProperty("name", out var n) && n.TryGetProperty("formatted_name", out var full) ? full.GetString() : "جهة اتصال";
                    var phone = contact.TryGetProperty("phones", out var phones) && phones.GetArrayLength() > 0 && phones[0].TryGetProperty("phone", out var number) ? number.GetString() : "";
                    yield return (id.GetString()!, from.GetString()!, $"{name} {phone}".Trim());
                }
            }
        }
    }
}
