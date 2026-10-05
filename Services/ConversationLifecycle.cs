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
    public static string EnglishDigits(string text) => string.Concat(text.Select(c =>
        c >= '٠' && c <= '٩' ? (char)('0' + c - '٠') :
        c >= '۰' && c <= '۹' ? (char)('0' + c - '۰') : c));
    public static string MaskPhone(string phone)
    {
        var digits = EnglishDigits(phone).Trim().TrimStart('+');
        if (digits.StartsWith("966")) digits = "0" + digits[3..];
        return Regex.IsMatch(digits, @"^05[0-9]{8}$")
            ? digits[..3] + " XXXX " + digits[^4..] : "XXXX";
    }
    // Remove sentence-ending periods while preserving decimals, abbreviations and URLs.
    public static string Format(string text) => Regex.Replace(EnglishDigits(text), @"(?<!\d)\.(?=[*_~]*(?:\s|$))", "").Trim();
}
