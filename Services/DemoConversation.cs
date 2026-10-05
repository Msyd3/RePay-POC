using System.Globalization;
using System.Text.RegularExpressions;
namespace RePay.WhatsAppPoc.Services;

public sealed class DemoState
{
    public string Kind { get; set; } = "";
    public string Stage { get; set; } = "";
    public string? Recipient { get; set; }
    public string? Product { get; set; }
}

public static class DemoConversation
{
    public static bool TryReply(ConversationState state, string input, out string reply)
    {
        reply = "";
        var text = input.Trim();
        var demo = state.Demo;
        if (demo is not null && Regex.IsMatch(text, @"^(إلغاء|الغاء|خلاص|وقف|توقف|الغها)$"))
        { state.Demo = null; reply = "تم إلغاء المثال، ما تمت أي عملية."; return true; }
        // Questions go to the conversational model without being mistaken for a recipient or product.
        if (Regex.IsMatch(text, @"^(كيف|وش|ايش|هل|متى|ليش|ما|ماهي|ماهو|كم رسوم|ما ابي|ما أبي|لا اريد|لا أريد)\b") || ServiceConversation.IsNameChange(text)) return false;
        if (demo is null)
        {
            var transfer = Regex.IsMatch(text, @"(?:حول|حوّل|احول|أحول|نجرب التحويل|جرّب تحويل|جرب تحويل|مثال.*تحويل|تجربة.*تحويل)");
            var shopping = Regex.IsMatch(text, @"(?:ابحث|دور|دوّر|ادور|أدور|اشتري|أشتري|ادفع|أدفع|نجرب الدفع|جرّب شراء|جرب شراء|مثال.*(?:دفع|شراء|بحث)|تجربة.*(?:دفع|شراء|بحث))");
            if (!transfer && !shopping) return false;
            demo = state.Demo = new DemoState { Kind = transfer ? "transfer" : "payment", Stage = transfer ? "recipient" : "product" };
            if (transfer) { state.TransferExamples++; reply = "نجرب مثال بدون تحويل فعلي. لمين بتحوّل؟ اكتب اسم الجهة أو رقم الجوال، أو شارك جهة الاتصال هنا."; }
            else { state.SearchExamples++; reply = "نجرب مثال شراء بدون بحث أو دفع فعلي. وش الغرض اللي تبيه؟ اذكر ميزانيتك إن حبيت؛ المتاجر السعودية هي الافتراضية، وتقدر تحدد متجرًا أو دولة ثانية."; }
            return true;
        }
        if (Regex.IsMatch(text, @"(?:رمز التحقق|كلمة المرور|رقم البطاقة|CVV|OTP)", RegexOptions.IgnoreCase))
        { reply = "لا ترسل بيانات دفع أو رموز تحقق. نستخدم تفاصيل افتراضية للمثال فقط."; return true; }
        if (text.Length > 250) { reply = "اختصر التفاصيل للمثال، ولا ترسل معلومات دفع أو رموز تحقق."; return true; }
        if (demo.Stage == "recipient")
        {
            if (text.Length < 2 || Regex.IsMatch(text, @"^(نعم|ايه|لا|تمام|طيب|شكرا)$")) { reply = "اكتب اسم المستفيد أو شارك جهة الاتصال عشان نكمل المثال."; return true; }
            demo.Recipient = text; demo.Stage = "amount";
            reply = "كم المبلغ بالريال؟ هذا مثال فقط."; return true;
        }
        if (demo.Stage == "amount")
        {
            var normalized = string.Concat(text.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c)).Replace('٫', '.');
            var match = Regex.Match(normalized, @"^\s*(\d{1,7}(?:\.\d{1,2})?)\s*(?:ريال|ر.س|SAR)?\s*$", RegexOptions.IgnoreCase);
            if (!match.Success || !decimal.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            { reply = "اكتب المبلغ مثل: 100 ريال."; return true; }
            reply = $"ملخص تجريبي:\nالمستفيد: {demo.Recipient}\nالمبلغ: {amount:0.##} ريال\nبعد تفعيل الخدمة، تراجع التفاصيل وتوافق قبل التحويل. ما تم تحويل أي مبلغ.";
            state.Demo = null; return true;
        }
        if (demo.Stage == "product")
        {
            demo.Product = text; demo.Stage = "store";
            reply = "عندك متجر محدد؟ اكتب اسمه أو رابطه، أو قل «سعودي» ونخلي نطاق المثال شركات سعودية."; return true;
        }
        if (demo.Stage == "store")
        {
            var scope = Regex.IsMatch(text, @"^(سعودي|سعودية|السعودية|ما عندي|ماعندي|لا|اي متجر)$") ? "الشركات السعودية" : text;
            state.PaymentExamples++;
            reply = $"ملخص شراء تجريبي:\nالطلب: {demo.Product}\nالمتجر أو نطاق البحث: {scope}\nالفكرة أدور لك غرضك وأجهّز الشراء حتى لو ما فيه تكامل مباشر، وأعرض السعر النهائي لموافقتك. هنا ما صار بحث أو شراء أو دفع فعلي.";
            state.Demo = null; return true;
        }
        return false;
    }
}
