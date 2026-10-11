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
    public decimal? Amount { get; set; }
    public decimal? Budget { get; set; }
    public bool BudgetCeiling { get; set; }
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
        var shopping = !question && Regex.IsMatch(clean, @"(?:\b(?:ابحث|دور|ادور|اشتري|ادفع|دفع|شراء)\b|(?:مثال|تجربة|جرب|نجرب).*(?:دفع|شراء|بحث))");
        // A fresh explicit request can replace an unfinished or confirmed example.
        if ((demo is null || demo.Stage == "confirm" || (transfer && demo.Kind != "transfer") || (shopping && demo.Kind != "payment")) && (transfer || shopping))
        {
            state.PendingFollowUp = null;
            demo = state.Demo = new DemoState { Kind = transfer ? "transfer" : "payment", Stage = transfer ? "recipient" : "product" };
            state.Welcomed = true;
            if (transfer) { state.TransferExamples++; reply = "*مثال تحويل — بدون تنفيذ*\n\nلمين بتحوّل؟ اكتب الاسم أو رقم الجوال، أو شارك جهة الاتصال هنا."; }
            else
            {
                state.SearchExamples++;
                // Preserve a product already supplied with a purchase request.
                var product = Regex.Match(clean, @"^(?:(?:ابي|ابغى|اريد)\s+)?(?:اشتري|ادفع قيمة|ابحث عن|دور لي|ادور على)\s+(.+)$");
                if (product.Success) return PurchaseSummary(state, demo, product.Groups[1].Value, out reply);
                reply = "*مثال شراء — بدون تنفيذ*\n\nوش المنتج اللي تبيه؟ تقدر تضيف اسم المتجر أو رابطه في نفس الرسالة. يكفي اسم المنتج";
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
            if (demo.Amount is not null) return TransferSummary(state, demo, demo.Amount.Value, out reply);
            reply = "*المبلغ بالريال السعودي*\nكم تبي تحوّل في المثال؟"; return true;
        }
        if (demo.Stage == "amount")
        {
            var normalized = string.Concat(text.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c)).Replace('٫', '.');
            normalized = Regex.Replace(normalized, @"^(?:المبلغ|حول|حوّل)\s+", "");
            var match = Regex.Match(normalized, @"^\s*(\d{1,7}(?:\.\d{1,2})?)\s*(?:ريال(?: سعودي)?|ر\.س|SAR)?\s*$", RegexOptions.IgnoreCase);
            if (!match.Success || !decimal.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            { reply = "المبالغ بالريال السعودي فقط. اكتب مثلًا: 100 ريال سعودي."; return true; }
            return TransferSummary(state, demo, amount, out reply);
        }
        if (demo.Stage is "product" or "store")
            return PurchaseSummary(state, demo, demo.Stage == "store" ? $"{demo.Product} — {text}" : text, out reply);
        return false;
    }

    public static bool PurchaseSummary(ConversationState state, DemoState demo, string product, out string reply)
    {
        if (product.Length > 250 || Regex.IsMatch(NameConversation.Normalize(product), @"^(نعم|ايه|تمام|طيب|تاكيد|اكد|موافق)$"))
        { reply = "اكتب اسم المنتج اللي تبيه للمثال."; return true; }
        if (PurchaseExample.TryBudget(product, out var requestedBudget, out var ceiling, out var onlyBudget))
        {
            demo.Budget = requestedBudget; demo.BudgetCeiling = ceiling;
            if (onlyBudget) return RebuildPurchase(state, demo, out reply);
        }
        demo.Product = SafeText(product);
        return RebuildPurchase(state, demo, out reply);
    }
    public static bool RebuildPurchase(ConversationState state, DemoState demo, out string reply)
    {
        state.PendingFollowUp = null;
        demo.Summary = null;
        demo.Amount = null;
        if (demo.Product is null)
        { demo.Stage = "product"; reply = "تمام، وش المنتج اللي تبيه؟"; return true; }
        var price = PurchaseExample.Select(demo.Product, demo.Budget, demo.BudgetCeiling);
        if (price is null)
        {
            demo.Stage = "product";
            reply = "في خيارات المثال ما فيه سعر قريب من ميزانيتك\nتبي نغيّر المنتج أو المواصفات؟ ما راح أفترض مبلغًا جديدًا";
            return true;
        }
        demo.Amount = price;
        var amount = price.Value.ToString("0.00", CultureInfo.InvariantCulture);
        var budgetLine = demo.Budget is null ? "" : $"\n*ميزانيتك:* {demo.Budget.Value.ToString("0.00", CultureInfo.InvariantCulture)} ريال سعودي";
        var choice = demo.Budget is null ? "اخترت مبلغ المثال تلقائيًا"
            : price == demo.Budget ? "خيار المثال يطابق المبلغ اللي طلبته"
            : $"أقرب خيار في المثال، الفرق {Math.Abs(price.Value - demo.Budget.Value).ToString("0.00", CultureInfo.InvariantCulture)} ريال " + (price > demo.Budget ? "أعلى من ميزانيتك، يحتاج موافقتك" : "أقل من ميزانيتك");
        demo.Summary = $"*العملية:* شراء تجريبي\n*الحساب المصدر:* حساب ري باي (افتراضي للمثال)\n*المنتج وطلبك:* {demo.Product}{budgetLine}\n*مبلغ توضيحي فقط:* {amount} ريال سعودي (ليس سعرًا فعليًا للمنتج)\n{choice}";
        demo.Stage = "confirm";
        state.PaymentExamples++;
        reply = $"*مثال على طلبك*\n\n{demo.Summary}\n\nالفكرة أدور لك غرضك وأجهّز الشراء، وأعرض التفاصيل والسعر النهائي لموافقتك؛ ما يشترط تكاملًا مباشرًا مع المتجر.\n\n_هنا ما صار بحث أو شراء أو دفع فعلي._";
        state.PendingFollowUp = "تؤكد ملخص مثال الشراء؟\nاكتب *تأكيد* أو *إلغاء*، وبعدها نبدأ طلبًا جديدًا. التأكيد هنا للمثال فقط.";
        return true;
    }
    private static bool TransferSummary(ConversationState state, DemoState demo, decimal amount, out string reply)
    {
        demo.Amount = amount;
        demo.Summary = $"*العملية:* تحويل تجريبي\n*الحساب المصدر:* حساب ري باي (افتراضي للمثال)\n*المستفيد:* {demo.Recipient}\n*المبلغ:* {amount.ToString("0.00", CultureInfo.InvariantCulture)} ريال سعودي";
        demo.Stage = "confirm";
        reply = $"*ملخص التحويل*\n\n{demo.Summary}\n\n_مثال توضيحي، ما تم تحويل أي مبلغ._";
        state.PendingFollowUp = "هل تؤكد هذا المثال؟\nاكتب *تأكيد* أو *إلغاء*. بعدها نبدأ طلبًا جديدًا، بدون تنفيذ مالي.";
        return true;
    }
    private static string SafeText(string text) => text.Replace("*", "").Replace("_", "").Replace("~", "").Replace("`", "").Replace('\n', ' ').Replace('\r', ' ');
}
