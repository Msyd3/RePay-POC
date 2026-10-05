using System.Globalization;
using System.Text.RegularExpressions;
namespace RePay.WhatsAppPoc.Services;

public sealed class DemoState
{
    public string Kind { get; set; } = "";
    public string Stage { get; set; } = "";
    public string? Recipient { get; set; }
    public string? Product { get; set; }
    public string? Summary { get; set; }
}

public static class DemoConversation
{
    public static bool TryReply(ConversationState state, string input, out string reply)
    {
        reply = "";
        var text = input.Trim();
        var clean = NameConversation.Normalize(text);
        var demo = state.Demo;
        if (demo is not null && Regex.IsMatch(clean, @"^(الغاء|خلاص|وقف|توقف|الغها|لا)$"))
        { state.Demo = null; reply = "*تم إلغاء المثال*\nما تمت أي عملية. اكتب طلبك الجديد."; return true; }
        if (demo?.Stage == "confirm" && Regex.IsMatch(clean, @"^(نعم|ايه|ايوه|موافق|اوافق|تاكيد|اكد|تمام|تم|اكيد|نفذ)$"))
        {
            reply = $"*تم تأكيد المثال فقط ✓*\n\n{demo.Summary}\n\nما تم تحويل أو دفع أي مبلغ.\nجاهز لطلب جديد، وش تبي نجرب؟";
            state.Demo = null; return true;
        }
        if (NameConversation.IsRequest(text)) return false;
        var question = Regex.IsMatch(clean, @"^(كيف|وش|ايش|هل|متى|ليش|ما|ماهي|ماهو|كم رسوم)\b");
        var transfer = !question && Regex.IsMatch(clean, @"(?:\b(?:حول|احول|تحويل)\b|(?:مثال|تجربة|جرب|نجرب).*تحويل)");
        var shopping = !question && Regex.IsMatch(clean, @"(?:\b(?:ابحث|دور|ادور|اشتري|ادفع)\b|(?:مثال|تجربة|جرب|نجرب).*(?:دفع|شراء|بحث))");
        // A fresh explicit request can replace an unfinished or confirmed example.
        if ((demo is null || demo.Stage == "confirm") && (transfer || shopping))
        {
            demo = state.Demo = new DemoState { Kind = transfer ? "transfer" : "payment", Stage = transfer ? "recipient" : "product" };
            state.Welcomed = true;
            if (transfer) { state.TransferExamples++; reply = "*مثال تحويل — بدون تنفيذ*\n\nلمين بتحوّل؟ اكتب الاسم أو رقم الجوال، أو شارك جهة الاتصال هنا."; }
            else
            {
                state.SearchExamples++;
                // Preserve a product already supplied with a purchase request.
                var product = Regex.Match(clean, @"^(?:(?:ابي|ابغى|اريد)\s+)?(?:اشتري|ادفع قيمة|ابحث عن|دور لي|ادور على)\s+(.+)$");
                if (product.Success) return PurchaseSummary(state, demo, product.Groups[1].Value, out reply);
                reply = "*مثال شراء — بدون تنفيذ*\n\nوش المنتج اللي تبيه؟ تقدر تضيف اسم المتجر أو رابطه في نفس الرسالة. يكفي اسم المنتج؛ النطاق الافتراضي شركات سعودية أو متاجر تخدم السعودية.";
            }
            return true;
        }
        if (demo is null) return false;
        if (question) return false;
        if (Regex.IsMatch(text, @"(?:رمز التحقق|كلمة المرور|رقم البطاقة|CVV|OTP)", RegexOptions.IgnoreCase))
        { reply = "لا ترسل بيانات دفع أو رموز تحقق. نستخدم تفاصيل افتراضية للمثال فقط."; return true; }
        if (text.Length > 250) { reply = "اختصر تفاصيل المثال، ولا ترسل بيانات دفع أو رموز تحقق."; return true; }
        if (demo.Stage == "confirm")
        { reply = "لتأكيد المثال قل «تأكيد»، أو اكتب طلبًا جديدًا. ما راح تنفّذ أي عملية مالية."; return true; }
        if (demo.Stage == "recipient")
        {
            if (text.Length < 2 || Regex.IsMatch(clean, @"^(نعم|ايه|تمام|طيب|شكرا)$")) { reply = "اكتب اسم المستفيد أو شارك جهة الاتصال عشان نكمل المثال."; return true; }
            demo.Recipient = SafeText(text); demo.Stage = "amount";
            reply = "*المبلغ بالريال السعودي*\nكم تبي تحوّل في المثال؟"; return true;
        }
        if (demo.Stage == "amount")
        {
            var normalized = string.Concat(text.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c)).Replace('٫', '.');
            normalized = Regex.Replace(normalized, @"^(?:المبلغ|حول|حوّل)\s+", "");
            var match = Regex.Match(normalized, @"^\s*(\d{1,7}(?:\.\d{1,2})?)\s*(?:ريال(?: سعودي)?|ر\.س|SAR)?\s*$", RegexOptions.IgnoreCase);
            if (!match.Success || !decimal.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            { reply = "المبالغ بالريال السعودي فقط. اكتب مثلًا: 100 ريال سعودي."; return true; }
            demo.Summary = $"*العملية:* تحويل تجريبي\n*المستفيد:* {demo.Recipient}\n*المبلغ:* {amount.ToString("0.00", CultureInfo.InvariantCulture)} ريال سعودي";
            demo.Stage = "confirm";
            reply = $"*ملخص التحويل*\n\n{demo.Summary}\n\n_مثال توضيحي، ما تم تحويل أي مبلغ._";
            state.PendingFollowUp = "هل تؤكد هذا المثال؟\nاكتب *تأكيد* أو *إلغاء*. بعدها نبدأ طلبًا جديدًا، بدون تنفيذ مالي.";
            return true;
        }
        if (demo.Stage is "product" or "store")
            return PurchaseSummary(state, demo, demo.Stage == "store" ? $"{demo.Product} — {text}" : text, out reply);
        return false;
    }

    private static bool PurchaseSummary(ConversationState state, DemoState demo, string product, out string reply)
    {
        if (product.Length > 250 || Regex.IsMatch(product, @"^(نعم|ايه|تمام|طيب)$"))
        { reply = "اكتب اسم المنتج اللي تبيه للمثال."; return true; }
        demo.Product = SafeText(product);
        demo.Summary = $"*العملية:* شراء تجريبي\n*المنتج وطلبك:* {demo.Product}\n*نطاق البحث المقترح:* الشركات السعودية أو المتاجر التي تخدم السعودية؛ يُقدّم متجرك المحدد إن ذكرته\n*مبلغ توضيحي فقط:* 100.00 ريال سعودي (ليس سعر المنتج)";
        demo.Stage = "confirm";
        state.PaymentExamples++;
        reply = $"*مثال على طلبك*\n\n{demo.Summary}\n\nالفكرة أدور لك غرضك وأجهّز الشراء، وأعرض التفاصيل والسعر النهائي لموافقتك؛ ما يشترط تكاملًا مباشرًا مع المتجر.\n\n_هنا ما صار بحث أو شراء أو دفع فعلي._";
        state.PendingFollowUp = "تؤكد ملخص مثال الشراء؟\nاكتب *تأكيد* أو *إلغاء*، وبعدها نبدأ طلبًا جديدًا. التأكيد هنا للمثال فقط.";
        return true;
    }
    private static string SafeText(string text) => text.Replace("*", "").Replace("_", "").Replace("~", "").Replace("`", "").Replace('\n', ' ').Replace('\r', ' ');
}
