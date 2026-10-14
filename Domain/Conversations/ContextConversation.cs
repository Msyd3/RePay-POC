using System.Text.Json;
using System.Text.RegularExpressions;
namespace RePay.WhatsAppPoc.Services;
public static class ContextConversation
{
    public static Task<string> ReplyAsync(ConversationState state, string input, AgentRouter agent, CancellationToken ct) => TransferConversation.ReplyAsync(state, input, agent, ct);
    public static string Context(ConversationState state, string input) => JsonSerializer.Serialize(new
    {
        name = state.Name,
        conversationState = new { language = state.Language, focus = state.Focus, awaitingName = state.AwaitingNameChange,
            task = state.Transfer is null ? null : new { kind = "transfer", recipient = state.Transfer.RecipientName,
                hasPhone = state.Transfer.RecipientPhone is not null, amount = state.Transfer.Amount, requestedField = state.Transfer.RequestedField } },
        recentConversation = state.Turns.TakeLast(8).Select(t => new ChatTurn(t.Role, Redact(t.Text))),
        currentMessage = Redact(input)
    });
    private static string Redact(string text) => Regex.Replace(text, @"[+＋]?[0-9٠-٩۰-۹][0-9٠-٩۰-۹ ()-]{7,}[0-9٠-٩۰-۹]", "[رقم جوال]");
}
