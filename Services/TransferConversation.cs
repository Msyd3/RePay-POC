using System.Globalization;
using System.Text.RegularExpressions;

namespace RePay.WhatsAppPoc.Services;

public sealed class TransferDraft
{
    public string? RecipientName { get; set; }
    public string? RecipientPhone { get; set; }
    public decimal? Amount { get; set; }
    public string? Note { get; set; }
    public decimal? RecordedAmount { get; set; }
    public string? ReviewId { get; set; }
    public DateTimeOffset? ReviewExpiresAt { get; set; }
    public string? RequestedField { get; set; }
}

public static class TransferConversation
{
    public static string Say(ConversationState s, string ar, string en) => s.Language == "en" ? en : ar;
    public static void DetectLanguage(ConversationState s, string input)
    {
        if (input.StartsWith("contact:") || input.StartsWith("review:")) return;
        var ar = Regex.Matches(input, @"[\u0621-\u064A]").Count;
        var en = Regex.Matches(input, @"[A-Za-z]").Count;
        var englishRequest = Regex.IsMatch(input.Trim(), @"^(?:what|how|why|when|can|send|transfer|change|edit|update|hello|hi|hey)\b", RegexOptions.IgnoreCase);
        if (ar > 0 || en > 0) s.Language = ar > 0 && !englishRequest ? "ar" : "en";
    }
    public static async Task<string> ReplyAsync(ConversationState s, string input, AgentRouter agent, CancellationToken ct)
    {
        DetectLanguage(s, input);
        s.PendingReviewButton = null;
        s.Demo = null; // Retire purchase and legacy demo state on upgrade.
        var text = ConversationLifecycle.EnglishDigits(NameConversation.Normalize(input));
        var draft = s.Transfer;
        if (Regex.IsMatch(text, @"^(?:(?:وش|ايش|ما)\s+(?:هي\s+|هو\s+)?(?:ري\s*باي|ريبای|ريبـاي|repay)|what(?:'s| is)\s+repay)\s*[?؟]?$", RegexOptions.IgnoreCase))
            return Say(s, "ري باي هي محفظة رقمية تجعل عمليات التحويل سهلة زي إرسال رسالة", "RePay is a digital wallet that makes sending money as easy as sending a message");
        if (ServiceConversation.IsCasualMessage(input) && Regex.IsMatch(text, @"^(?:يا\s+)?(?:هلا|اهلا|اهلين|مرحبا|السلام عليكم|hello|hi|hey)\b", RegexOptions.IgnoreCase))
            return Welcome(s);
        if (input.StartsWith("review:"))
        {
            if (draft?.ReviewId != input[7..] || draft.ReviewExpiresAt <= DateTimeOffset.UtcNow)
                return Say(s, "زر المراجعة انتهى أو تغيّرت التفاصيل، قل «كمل» لعرضها مجددًا", "This review expired or the details changed. Say 'continue' to review again");
            return Say(s, "بياناتك جاهزة للمراجعة. صفحة التأكيد بنضيفها لاحقًا؛ ما تم تحويل أي مبلغ", "Your details are ready for review. The confirmation page is coming later; no money has been sent");
        }
        if (Regex.IsMatch(text, @"^(?:الغاء|الغها|كنسل|وقف|توقف|خلاص|cancel|stop|no)$", RegexOptions.IgnoreCase))
        {
            s.Transfer = null; s.AwaitingNameChange = false;
            return Say(s, "تم إلغاء الطلب، ما تم تحويل أي مبلغ", "Request cancelled. No money has been sent");
        }
        var rename = NameConversation.IsRequest(input) || Regex.IsMatch(text, @"^(?:(?:please|i want to)\s+)?(?:change|edit|update)\s+(?:my\s+)?name\b|^(?:my name is|i am|I'm)\s", RegexOptions.IgnoreCase);
        if (rename)
        {
            var candidate = Regex.Replace(text, @"^(?:(?:please|i want to)\s+)?(?:change|edit|update)\s+(?:my\s+)?name\s*(?:to\s+)?|^(?:my name is|i am|I'm)\s+|^(?:(?:ابي|ابغى|اريد|ممكن|لو سمحت)\s+)?(?:اغير|غير|اعدل|عدل|تعديل)\s+(?:اسمي|اسم الحساب)\s*(?:الى\s+)?|^اسمي\s+", "", RegexOptions.IgnoreCase);
            Invalidate(draft);
            s.AwaitingNameChange = true;
            if (NameConversation.TryFullName(candidate, out var name))
            { s.Name = name; s.AwaitingNameChange = false; return draft is null ? Welcome(s) : Next(s); }
            return Say(s, "وش اسمك الثنائي؟ الاسم الأول واسم العائلة", "What's your full name? First and last name");
        }
        if (s.AwaitingNameChange || draft?.RequestedField == "sender")
        {
            if (NameConversation.TryFullName(input, out var name))
            { s.Name = name; s.AwaitingNameChange = false; Invalidate(draft); return draft is null ? Welcome(s) : Next(s); }
            if (ServiceConversation.TryGetName(input, out _)) return Say(s, "اكتب اسمك الأول واسم العائلة عشان نكمل", "Please enter your first and last name to continue");
        }
        if (Regex.IsMatch(text, @"^(?:ما ابي احول|ما ابغى احول|لا تحول|don't send|do not send|don't transfer)\b", RegexOptions.IgnoreCase))
        { s.Transfer = null; return Say(s, "تم إلغاء الطلب، ما تم تحويل أي مبلغ", "Request cancelled. No money has been sent"); }
        if (input == "multiple_contacts") return Say(s, "شارك جهة اتصال واحدة وحدد المستفيد", "Please share one contact and specify the recipient");
        if (input.Length > 250) return Say(s, "اختصر التفاصيل إلى اسم المستفيد وجواله والمبلغ، والملاحظة اختيارية", "Keep the details to recipient, mobile number, amount, and an optional short note");
        var phoneCandidate = Regex.Match(text, @"(?<!\d)(?:\+?966[\s-]*5(?:[\s-]*\d){8}|05(?:[\s-]*\d){8})(?!\d)");
        if (!phoneCandidate.Success) phoneCandidate = Regex.Match(text, @"(?<!\d)(?:\+?966|05)[0-9 \-]*[0-9](?!\d)");
        if (!phoneCandidate.Success && Regex.IsMatch(text, @"^\d{8,}$")) phoneCandidate = Regex.Match(text, @"^\d+$");
        if (phoneCandidate.Success)
        {
            var digits = Regex.Replace(phoneCandidate.Value, @"\D", "");
            var local = digits.StartsWith("966") ? "0" + digits[3..] : digits;
            if (!Regex.IsMatch(local, @"^05\d{8}$"))
                return local.Length < 10
                    ? Say(s, $"رقم الجوال ناقص {10 - local.Length} خانات؛ اكتب الرقم كاملًا من 10 خانات ويبدأ بـ05", $"The mobile number is missing {10 - local.Length} digits. Enter all 10 digits, starting with 05")
                    : Say(s, "رقم الجوال لازم يكون 10 خانات ويبدأ بـ05", "The mobile number must have 10 digits and start with 05");
        }
        var withoutPhone = phoneCandidate.Success ? text.Remove(phoneCandidate.Index, phoneCandidate.Length) : text;
        var allowBareAmount = draft?.RequestedField == "amount" || Regex.IsMatch(text, @"\b(?:حول|احول|حوله|send|transfer|المبلغ|amount)\b", RegexOptions.IgnoreCase);
        if (SendAmount.TryExtract(withoutPhone, allowBareAmount, out var suppliedAmount, out _) && !SendAmount.Valid(suppliedAmount))
            return Say(s, "الحد الأدنى للتحويل 1 ريال. اكتب مبلغًا من ريال واحد وبحد أقصى منزلتين عشريتين", "The minimum send amount is 1 SAR. Enter at least 1 SAR with at most two decimal places");
        var question = Regex.IsMatch(text, @"^(?:كيف|وش|ايش|هل|متى|ليش|كم رسوم|how|what|when|why|can you)\b", RegexOptions.IgnoreCase);
        var start = !question && Regex.IsMatch(text, @"\b(?:حول|احول|حوله|تحويل|send|transfer)\b", RegexOptions.IgnoreCase);
        var editAmount = !question && Regex.IsMatch(text, @"^(?:(?:ابي|ابغى|اريد|ممكن|please|i want to)\s+)?(?:عدل|اعدل|غير|اغير|change|edit|update)\s+(?:المبلغ|مبلغ|(?:the\s+)?amount)\b", RegexOptions.IgnoreCase);
        if (editAmount && draft is not null)
        {
            draft.Amount = null;
            draft.RequestedField = "amount";
            Invalidate(draft);
            Extract(draft, text, false);
            return Next(s);
        }
        var contact = input.StartsWith("contact:");
        var hasPhone = Regex.IsMatch(text, @"(?<!\d)(?:\+?966[\s-]*5(?:[\s-]*\d){8}|05(?:[\s-]*\d){8})(?!\d)");
        if (Regex.IsMatch(text, @"\b(?:شراء|اشتري|ادفع|دفع|buy|purchase|shopping|pay)\b", RegexOptions.IgnoreCase))
            return Say(s, "ري باي هنا للتحويل فقط. تقدر تقول: حول لمحمد 20 ريال", "You can send money with RePay here. You can say: send Mohammed 20 SAR");
        var namedRecipient = draft is null && s.Name is not null && !s.AwaitingNameChange && !question && ServiceConversation.TryGetName(input, out _);
        if (start || contact || namedRecipient || hasPhone && !question || draft is not null && !question)
        {
            // A completed draft is a separate request from a fresh recipient/number/start.
            // Pronoun + amount while still collecting fields continues the current request.
            if (draft?.RequestedField == "review" && (start || contact || hasPhone))
                draft = s.Transfer = null;
            if (draft is null) { draft = s.Transfer = new(); s.TransferExamples++; }
            if (start) s.AwaitingNameChange = false;
            if (Regex.IsMatch(text, @"^(?:كمل|نكمل|continue|resume)$", RegexOptions.IgnoreCase)) return Next(s);
            if (Regex.IsMatch(text, @"^(?:تاكيد|اكد|نعم|تمام|confirm|yes|ok)$", RegexOptions.IgnoreCase))
                return draft.ReviewId is null ? Next(s) : Say(s, "استخدم زر «مراجعة التحويل». ما تم تحويل أي مبلغ", "Use the 'Review send' button. No money has been sent");
            var changed = Extract(draft, text, contact);
            if (!changed && draft.RequestedField == "sender") return Say(s, "اكتب اسمك الأول واسم العائلة عشان نكمل", "Please enter your first and last name to continue");
            if (!changed && draft.RequestedField == "recipientName" && ServiceConversation.TryGetName(input, out var recipient))
            { draft.RecipientName = recipient; changed = true; }
            if (changed || start || contact)
            {
                if (draft.RecipientName is not null && draft.RecipientPhone is not null && (contact || hasPhone))
                {
                    s.Contacts.RemoveAll(c => c.Name.Equals(draft.RecipientName, StringComparison.OrdinalIgnoreCase) && c.Phone == draft.RecipientPhone);
                    s.Contacts.Add(new(draft.RecipientName, draft.RecipientPhone));
                    s.Contacts = s.Contacts.TakeLast(20).ToList();
                }
                Invalidate(draft); return Next(s);
            }
        }
        // Resolve ambiguous edits in the current conversation, without treating them as slot values.
        if (draft is not null || Regex.IsMatch(text, @"\b(?:اعدله|عدله|قصدي|اقصد|edit|change|instead)\b", RegexOptions.IgnoreCase))
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(20));
            IntentDecision decision;
            try { decision = await agent.InterpretAsync(ContextConversation.Context(s, input), budget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { decision = new("clarify", null); }
            if (decision.Value is not null && !input.Contains(decision.Value, StringComparison.Ordinal)) decision = new("clarify", null);
            if (decision.Action == "rename")
            {
                s.AwaitingNameChange = true; Invalidate(draft);
                if (decision.Value is not null && NameConversation.TryFullName(decision.Value, out var newName))
                { s.Name = newName; s.AwaitingNameChange = false; return draft is null ? Welcome(s) : Next(s); }
                return Say(s, "وش اسمك الثنائي الجديد؟", "What's your new first and last name?");
            }
            if (decision.Action == "start_transfer")
            { s.Transfer = new(); s.TransferExamples++; s.AwaitingNameChange = false; if (decision.Value is not null) Extract(s.Transfer, ConversationLifecycle.EnglishDigits(decision.Value), false); return Next(s); }
            if (decision.Action == "cancel") { s.Transfer = null; return Say(s, "تم إلغاء الطلب", "Request cancelled"); }
            if (draft is not null && decision.Action is "edit_recipient" or "edit_amount")
            {
                Invalidate(draft);
                if (decision.Action == "edit_recipient") { draft.RecipientName = null; draft.RecipientPhone = null; }
                else draft.Amount = null;
                if (decision.Value is not null) Extract(draft, ConversationLifecycle.EnglishDigits(decision.Value), false);
                return Next(s);
            }
            if (decision.Action == "new_topic") { s.Transfer = null; draft = null; }
            else if (decision.Action == "continue" && draft is not null && decision.Value is not null && Extract(draft, ConversationLifecycle.EnglishDigits(decision.Value), false))
            { Invalidate(draft); return Next(s); }
            else if (decision.Action is not "question") return Say(s, "تقصد تعديل اسمك، المستفيد، أو المبلغ؟", "Do you mean your name, the recipient, or the amount?");
        }
        if (ServiceConversation.IsCasualMessage(input))
        { return Welcome(s); }
        try { return await agent.ReplyAsync(ContextConversation.Context(s, input), ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return Say(s, "تعذّر الرد الآن، جرّب مرة ثانية", "I couldn't reply right now. Please try again"); }
    }
    public static string Welcome(ConversationState s)
    {
        s.Welcomed = true;
        if (s.Name is null || !NameConversation.TryFullName(s.Name, out _))
        { s.AwaitingNameChange = true; return Say(s, "يا هلا 👋🏼 وش اسمك الثنائي؟ الاسم الأول واسم العائلة", "Hi 👋🏼 What's your full name? First and last name"); }
        return Say(s, $"يا هلا {s.Name} 👋🏼\n\nللتحويل السريع حط اسم المستفيد أو رقم جواله أو شارك جهة الاتصال", $"Hi {s.Name} 👋🏼\n\nTo send money quickly, enter the recipient's name or mobile number, or share their contact");
    }
    private static string ContactKey(string name) => NameConversation.Normalize(name).ToLowerInvariant();
    private static string? FindPhone(ConversationState s, string name)
    {
        var key = ContactKey(name);
        var exact = s.Contacts.Where(c => ContactKey(c.Name) == key).Select(c => c.Phone).Distinct().ToList();
        if (exact.Count > 0) return exact.Count == 1 ? exact[0] : null;
        var candidates = s.Contacts.Where(c => ContactKey(c.Name).StartsWith(key + " ", StringComparison.Ordinal)).Select(c => c.Phone).Distinct().ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }
    public static string DisplayPhone(string phone)
    {
        var digits = ConversationLifecycle.EnglishDigits(phone).TrimStart('+');
        if (digits.StartsWith("966")) digits = "0" + digits[3..];
        return "\u2066" + digits + "\u2069";
    }
    public static bool Extract(TransferDraft draft, string input, bool contact)
    {
        var text = input;
        var oldName = draft.RecipientName;
        var mayName = contact || draft.RecipientName is null || draft.RequestedField == "recipientName" || Regex.IsMatch(input, @"\b(?:حول|احول|حوله|send|transfer|المستفيد|recipient)\b", RegexOptions.IgnoreCase);
        var changed = false;
        if (contact) text = text[8..];
        var phone = Regex.Match(text, @"(?<!\d)(?:\+?966[\s-]*5(?:[\s-]*\d){8}|05(?:[\s-]*\d){8})(?!\d)");
        if (phone.Success)
        { var digits = Regex.Replace(phone.Value, @"\D", ""); draft.RecipientPhone = digits.StartsWith("05") ? "966" + digits[1..] : digits; text = text.Remove(phone.Index, phone.Length); changed = true; }
        var note = Regex.Match(text, @"(?:\b(?:ملاحظة|ملاحظه|note|for)\s*[:：]?\s*)(.+)$", RegexOptions.IgnoreCase);
        if (note.Success) { draft.Note = Regex.Replace(note.Groups[1].Value.Trim(), @"[*_~`\r\n]", " "); text = text[..note.Index]; changed = true; }
        if (SendAmount.TryExtract(text, draft.RequestedField == "amount" || Regex.IsMatch(text, @"\b(?:حول|حوله|احول|send|transfer|المبلغ|amount)\b", RegexOptions.IgnoreCase), out var value, out var remaining) && SendAmount.Valid(value))
        { draft.Amount = value; text = remaining; changed = true; }
        text = Regex.Replace(text.Trim(), @"^(?:(?:ابي|ابغى|اريد|جرب|نجرب|please|i want to)\s+)*(?:حول|حوله|احول|تحويل|send|transfer)\s*(?:(?:الى|to)\s+|ل)?|^(?:غير|عدل|change|edit)\s+(?:المستفيد|مستفيد|recipient|المبلغ|amount)\s*(?:الى|to)?\s*", "", RegexOptions.IgnoreCase).Trim();
        text = Regex.Replace(text, @"^(?:رقم(?: جوال)?|جوال|mobile(?: number)?|phone(?: number)?)\s*[:：]?\s*$", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"^(?:الى|to)\s+", "", RegexOptions.IgnoreCase).Trim();
        if (mayName && !Regex.IsMatch(text, @"\b(?:قصدي|اقصد|اسمي|الاسم|اعدله|اعدلها|غيره|غيرها|بدل|instead|change|edit|my|name)\b", RegexOptions.IgnoreCase) && ServiceConversation.TryGetName(text, out var name) && !Regex.IsMatch(text, @"^(?:له|لها|him|her|it|money|ريال|SAR)$", RegexOptions.IgnoreCase))
        { draft.RecipientName = name; if (oldName is not null && oldName != name && !phone.Success) draft.RecipientPhone = null; changed = true; }
        return changed;
    }
    private static void Invalidate(TransferDraft? d) { if (d is not null) { d.ReviewId = null; d.ReviewExpiresAt = null; } }
    public static string Next(ConversationState s)
    {
        var d = s.Transfer!;
        s.Focus = "transfer";
        if (s.Name is null || !NameConversation.TryFullName(s.Name, out _))
        { d.RequestedField = "sender"; return Say(s, "عشان نكمل، وش اسمك الثنائي؟ الاسم الأول واسم العائلة", "To continue, what's your full name? First and last name"); }

        if (d.RecipientName is null && d.RecipientPhone is not null)
        {
            var names = s.Contacts.Where(c => c.Phone == d.RecipientPhone).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 1) d.RecipientName = names[0];
        }
        if (d.RecipientName is null && d.RecipientPhone is null) { d.RequestedField = "recipientName"; return Say(s, "وش اسم المستفيد؟ تقدر تشارك جهة الاتصال", "What's the recipient's name? You can share their contact"); }
        if (d.RecipientPhone is null && d.RecipientName is not null) d.RecipientPhone = FindPhone(s, d.RecipientName);
        if (d.RecipientPhone is null) { d.RequestedField = "recipientPhone"; return Say(s, $"وش رقم جوال {d.RecipientName}؟ أو شارك جهة الاتصال", $"What's {d.RecipientName}'s mobile number? Or share their contact"); }
        if (d.Amount is not null && !SendAmount.Valid(d.Amount.Value)) { d.Amount = null; Invalidate(d); }
        if (d.Amount is null) { d.RequestedField = "amount"; return Say(s, "كم تبي تحوّل بالريال السعودي؟", "How much would you like to send in SAR?"); }
        d.RequestedField = "review";
        s.RecordTransferAmount();
        d.ReviewId = Guid.NewGuid().ToString("N");
        d.ReviewExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3);
        s.PendingReviewButton = d.ReviewId;
        var amount = d.Amount.Value.ToString("0.00", CultureInfo.InvariantCulture);
        return Say(s,
            $"*ملخص التحويل*\n*المرسل:* {s.Name}\n*المستفيد:* {d.RecipientName ?? "رقم الجوال"}\n*الجوال:* {DisplayPhone(d.RecipientPhone)}\n*المبلغ:* {amount} ريال سعودي" + (d.Note is null ? "" : $"\n*الملاحظة:* {d.Note}") + "\nراجع البيانات أو اطلب تعديلها. ما تم تحويل أي مبلغ",
            $"*Send summary*\n*Sender:* {s.Name}\n*Recipient:* {d.RecipientName ?? "Mobile number"}\n*Mobile:* {DisplayPhone(d.RecipientPhone)}\n*Amount:* {amount} SAR" + (d.Note is null ? "" : $"\n*Note:* {d.Note}") + "\nReview the details or request an edit. No money has been sent");
    }
}
