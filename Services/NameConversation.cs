using System.Text.RegularExpressions;
namespace RePay.WhatsAppPoc.Services;

public static class NameConversation
{
    private const string Prefix = @"^(?:(?:انا|لو سمحت|ممكن|ابي|ابغى|اريد|ودي|لو تكرمت|لا)\s+){0,3}(?:(?:اغير|غير|تغير|تغيير|اعدل|عدل|تعدل|تعديل|صحح)\s+(?:اسمي|الاسم|اسم الحساب)|اسمي الصحيح|اسمي الجديد|نادني|ناديني|سميني|اسمي)";
    public static string Normalize(string text) => Regex.Replace(text.Trim().Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا'), @"[\u064B-\u065F\u0670ـ]", "").Trim(' ', '؟', '?', '!', '.', '،', ':');
    public static bool IsRequest(string text) => Regex.IsMatch(Normalize(text), Prefix + @"(?:\s|$)");
    public static bool TryReply(ConversationState state, string input, out string reply)
    {
        reply = "";
        var text = Normalize(input);
        if (Regex.IsMatch(text, @"^(?:وش|ايش|ما هو|تعرف|تذكر)\s+اسمي$|^اسمي$"))
        { reply = state.Name is null ? "ما حفظت اسمك للحين، وش اسمك الثنائي؟" : $"اسمك المحفوظ: *{state.Name}*."; return true; }
        if (state.AwaitingNameChange && Regex.IsMatch(text, @"^(الغاء|خلاص|لا|لا تغيره|لا تغير اسمي)$"))
        { state.AwaitingNameChange = false; reply = "تمام، اسمك يبقى مثل ما هو."; return true; }
        if (IsRequest(input))
        {
            state.Demo = null; // Profile editing must take precedence over a recipient/product step.
            state.AwaitingNameChange = true;
            var candidate = Regex.Replace(text, Prefix + @"\s*", "");
            candidate = Regex.Replace(candidate, @"^(?:الى|ليكون|هو)\s+|^ل(?=\p{L})", "");
            if (TryFullName(candidate, out var name))
            { state.Name = name; state.AwaitingNameChange = false; reply = $"تم تحديث اسمك إلى *{name}*."; }
            else reply = "أكيد، وش اسمك الثنائي الجديد؟";
            state.Welcomed = true;
            return true;
        }
        if (state.AwaitingNameChange)
        {
            if (TryFullName(input, out var name))
            { state.Name = name; state.AwaitingNameChange = false; reply = $"تم تحديث اسمك إلى *{name}*."; return true; }
            if (!Regex.IsMatch(text, @"^(كيف|وش|ايش|هل|متى|ليش|ابي|ابغى|جرب|حول|احول|تحويل|ادفع|دفع|اشتري|شراء)\b"))
            { reply = "اكتب اسمك الثنائي (الاسم الأول واسم العائلة)، أو قل «إلغاء»"; return true; }
            state.AwaitingNameChange = false;
        }
        return false;
    }
    public static bool TryFullName(string input, out string name)
    {
        name = input.Trim();
        if (Regex.IsMatch(Normalize(name), @"^(?:لا|مو|مش|ما|غير|الاسم|اسمي|ابي|ابغى|اريد|خلاص|عادي|اكيد|نعم|ايه|تمام|طيب)\b")) return false;
        if (Regex.IsMatch(Normalize(name), @"(?:احول|حول|تحويل|ادفع|دفع|شراء|اشتري|جرب|نجرب|منتج|ابحث|دور|كم|كيف|وش|ايش|هل|متى)")) return false;
        return ServiceConversation.TryGetName(name, out name) && name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }
}
