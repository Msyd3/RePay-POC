using System.Text.RegularExpressions;
namespace RePay.WhatsAppPoc.Services;
public static class ConversationLifecycle
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
    public static bool Expire(ConversationState state, DateTimeOffset now)
    {
        if (state.LastMessageAt is null || now - state.LastMessageAt < IdleTimeout) return false;
        state.Demo = null;
        state.AwaitingNameChange = false;
        state.Turns.Clear();
        state.PendingFollowUp = null;
        return true;
    }
    // Remove sentence-ending periods while preserving decimals, abbreviations and URLs.
    public static string Format(string text) => Regex.Replace(text, @"(?<!\d)\.(?=[*_~]*(?:\s|$))", "").Trim();
}
