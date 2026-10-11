using System.Text.Json;
using System.Text.RegularExpressions;

namespace RePay.WhatsAppPoc.Services;

public static class ContextConversation
{
    public static async Task<string> ReplyAsync(ConversationState state, string input, AgentRouter agent, CancellationToken ct)
    {
        var text = NameConversation.Normalize(input);
        if (state.AwaitingClarification && Regex.IsMatch(text, @"^(?:اسمي|اسم الحساب|اسم صاحب الحساب)$"))
        {
            ResetTask(state);
            state.Focus = "profile";
            NameConversation.TryReply(state, "ابي اغير اسمي", out var clarificationReply);
            return clarificationReply;
        }
        // Unambiguous commands and validated slot values need no model round trip.
        if ((NameConversation.IsRequest(input) && !Regex.IsMatch(text, @"\b(?:مستفيد|المستفيد|مستلم|المستلم|الاسم)\b"))
            || Regex.IsMatch(text, @"^(?:وش|ايش|ما هو|تعرف|تذكر)\s+اسمي$"))
        {
            state.Focus = "profile";
            state.AwaitingClarification = false;
            if (NameConversation.TryReply(state, input, out var nameReply)) return nameReply;
        }
        if (Regex.IsMatch(text, @"^(?:الغاء|الغها|كنسل|وقف|توقف|خلاص|لا)$") && (state.Demo is not null || state.AwaitingNameChange))
            return Cancel(state);
        if (!state.AwaitingClarification && state.Demo is { } resume && Regex.IsMatch(text, @"^(?:كمل|نكمل|كمل الطلب|نكمل التحويل|نكمل الدفع)$"))
        {
            state.Focus = resume.Kind;
            if (resume.Stage == "confirm")
            {
                state.PendingFollowUp = "هل تؤكد هذا المثال؟ اكتب تأكيد أو إلغاء";
                return (resume.Summary ?? "") + "\nهذا مثال فقط بدون تنفيذ مالي";
            }
            return resume.Stage switch { "recipient" => "لمين تبي تحوّل في المثال؟", "amount" => "كم المبلغ بالريال السعودي؟", _ => "وش المنتج اللي تبيه؟" };
        }

        var start = ExplicitStart(text);
        if (start is not null)
        {
            ResetTask(state);
            state.Focus = start;
            if (DemoConversation.TryReply(state, input, out var startReply)) return startReply;
        }
        if (!state.AwaitingClarification && state.Demo is { } demo)
        {
            var slot = demo.Stage == "amount" && Regex.IsMatch(ConversationLifecycle.EnglishDigits(text), @"^\d+(?:[.٫]\d{1,2})?\s*(?:ريال(?: سعودي)?|SAR|ر\.س)?$", RegexOptions.IgnoreCase)
                || demo.Stage == "recipient" && Regex.IsMatch(text, @"(?:\+?9665\d{8}|05\d{8})")
                || demo.Stage == "confirm" && (Regex.IsMatch(text, @"^(?:تاكيد|اكد)$")
                    || Regex.IsMatch(text, @"^(?:نعم|ايه|ايوه|موافق|تمام|اكيد|نفذ)$")
                    && (state.Turns.LastOrDefault(t => t.Role == "assistant")?.Text.Contains("تؤكد") ?? false));
            if (slot && DemoConversation.TryReply(state, input, out var slotReply))
            { state.Focus = demo.Kind; return slotReply; }
        }

        var needsContext = state.Demo is not null || state.AwaitingNameChange || state.AwaitingClarification
            || Regex.IsMatch(text, @"\b(?:اعدله|اعدلها|اعدل|عدله|عدلها|غير|الاسم|غيره|غيرها|اغيره|اغيرها|غيرت|اقصد|قصدي|بدل|وقف|توقف|كنسل|كمل|نكمل|احول|ادفع|اشتري)\b");
        if (needsContext)
        {
            IntentDecision decision;
            using var intentBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            intentBudget.CancelAfter(TimeSpan.FromSeconds(20));
            try { decision = await agent.InterpretAsync(Context(state, input), intentBudget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { decision = new("clarify", null); }
            // A model may only use a detail copied from the current user message.
            if (decision.Value is not null && !input.Contains(decision.Value, StringComparison.Ordinal))
                decision = new("clarify", null);
            switch (decision.Action)
            {
                case "cancel": return Cancel(state);
                case "rename":
                    ResetTask(state);
                    state.Focus = "profile";
                    NameConversation.TryReply(state, decision.Value is null ? "ابي اغير اسمي" : "غير اسمي إلى " + decision.Value, out var renameReply);
                    return renameReply;
                case "start_transfer":
                case "start_payment":
                    ResetTask(state);
                    state.Focus = decision.Action == "start_transfer" ? "transfer" : "payment";
                    DemoConversation.TryReply(state, decision.Action == "start_transfer" ? "جرب تحويل" : "جرب شراء", out var startReply);
                    if (decision.Value is not null && DemoConversation.TryReply(state, decision.Value, out var detailReply)) return detailReply;
                    return startReply;
                case "edit_recipient":
                case "edit_amount":
                case "edit_product":
                    if (state.Demo is null || decision.Action == "edit_product" && state.Demo.Kind != "payment"
                        || decision.Action != "edit_product" && state.Demo.Kind != "transfer") return Clarify(state);
                    state.Demo.Summary = null;
                    state.PendingFollowUp = null;
                    state.AwaitingClarification = false;
                    state.Demo.Stage = decision.Action switch { "edit_recipient" => "recipient", "edit_amount" => "amount", _ => "product" };
                    state.Focus = state.Demo.Kind;
                    if (decision.Value is not null && DemoConversation.TryReply(state, decision.Value, out var editReply)) return editReply;
                    return decision.Action switch
                    {
                        "edit_recipient" => "تمام، مين المستفيد الجديد؟",
                        "edit_amount" => "تمام، كم المبلغ الجديد بالريال السعودي؟",
                        _ => "تمام، وش المنتج الجديد؟"
                    };
                case "continue":
                    if (state.AwaitingClarification && Regex.IsMatch(text, @"^(?:تاكيد|اكد|نعم|تمام|ايه|اكيد|موافق)$")) return Clarify(state);
                    state.AwaitingClarification = false;
                    if (state.AwaitingNameChange && NameConversation.TryFullName(decision.Value ?? input, out _)
                        && NameConversation.TryReply(state, decision.Value ?? input, out var continuation)) return continuation;
                    if (state.Demo is not null)
                    {
                        // Never let a vague control phrase become recipient, product, or confirmation.
                        var value = decision.Value ?? input;
                        if (Regex.IsMatch(text, @"^(?:كمل|نكمل|تمام|نعم|ايه|طيب)$")
                            || value == input && Regex.IsMatch(text, @"\b(?:ابي|ابغى|اعدله|اعدلها|اعدل|عدله|عدلها|غير|غيرت|غيره|غيرها|اغيره|اغيرها|قصدي|اقصد|بدل|لا|مو|وقف|توقف|كنسل)\b"))
                            return Clarify(state);
                        if (DemoConversation.TryReply(state, value, out var demoReply)) return demoReply;
                    }
                    return Clarify(state);
                case "new_topic":
                    ResetTask(state);
                    state.Focus = "conversation";
                    return await AnswerAsync(state, input, agent, ct);
                case "question":
                    state.AwaitingClarification = false;
                    return await AnswerAsync(state, input, agent, ct);
                default: return Clarify(state);
            }
        }

        if (NameConversation.TryReply(state, input, out var normalReply)) return normalReply;
        if (!state.Welcomed && state.Name is null)
        { state.Welcomed = true; state.Focus = "profile"; return ServiceConversation.Welcome; }
        if (state.Name is null && ServiceConversation.TryGetName(input, out var name))
        {
            state.Focus = "profile";
            if (NameConversation.TryFullName(name, out var fullName))
            { state.Name = fullName; return $"تشرفنا يا {fullName}، وش حاب نجرب؟"; }
            state.AwaitingNameChange = true;
            return "ممكن اسمك الثنائي؟ الاسم الأول واسم العائلة";
        }
        return await AnswerAsync(state, input, agent, ct);
    }

    private static string? ExplicitStart(string text)
    {
        if (Regex.IsMatch(text, @"\b(?:لا|مو|ما|بدل|بس|قصدي|كيف|وش|ايش|هل|متى|ليش|رسوم)\b")) return null;
        if (Regex.IsMatch(text, @"^(?:(?:ابي|ابغى|اريد|ودي)\s+)?(?:جرب|نجرب)\s+تحويل$|^(?:(?:ابي|ابغى|اريد|ودي)\s+)?احول(?:\s|$)")) return "transfer";
        if (Regex.IsMatch(text, @"^(?:(?:ابي|ابغى|اريد|ودي)\s+)?(?:جرب|نجرب)\s+(?:شراء|دفع)$|^(?:(?:ابي|ابغى|اريد|ودي)\s+)?(?:اشتري|ادفع|ابحث عن|دور لي|ادور على)(?:\s|$)")) return "payment";
        return null;
    }

    public static string Context(ConversationState state, string input) => JsonSerializer.Serialize(new
    {
        name = state.Name,
        conversationState = new { focus = state.Focus, awaitingName = state.AwaitingNameChange, awaitingClarification = state.AwaitingClarification,
            task = state.Demo is null ? null : new { kind = state.Demo.Kind, stage = state.Demo.Stage,
                recipient = Redact(state.Demo.Recipient ?? ""), product = Redact(state.Demo.Product ?? ""), amount = state.Demo.Amount } },
        recentConversation = state.Turns.TakeLast(8).Select(t => new ChatTurn(t.Role, Redact(t.Text))),
        currentMessage = Redact(input)
    });
    private static string Redact(string text) => Regex.Replace(text, @"[+＋]?[0-9٠-٩۰-۹][0-9٠-٩۰-۹ ()-]{7,}[0-9٠-٩۰-۹]", "[رقم جوال]");
    private static async Task<string> AnswerAsync(ConversationState state, string input, AgentRouter agent, CancellationToken ct)
    {
        try { return await agent.ReplyAsync(Context(state, input), ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return "تعذّر عليّ الرد الآن، جرّب ترسل سؤالك مرة ثانية"; }
    }
    private static void ResetTask(ConversationState state)
    {
        state.Demo = null;
        state.AwaitingNameChange = false;
        state.AwaitingClarification = false;
        state.PendingFollowUp = null;
    }
    private static string Cancel(ConversationState state)
    {
        ResetTask(state);
        state.Focus = null;
        return "تمام، وقفنا الطلب السابق بدون تنفيذ أي عملية\nوش طلبك الجديد؟";
    }
    private static string Clarify(ConversationState state)
    {
        state.AwaitingClarification = true;
        state.PendingFollowUp = null;
        return state.AwaitingNameChange || state.Focus == "profile"
            ? "تقصد تعديل اسمك، أو تبي نبدأ طلبًا جديدًا؟"
            : state.Demo?.Kind == "transfer"
                ? "تقصد اسمك، المستفيد، المبلغ، أو تبي توقف التحويل وتبدأ طلبًا جديدًا؟"
                : state.Demo?.Kind == "payment"
                    ? "تقصد تعديل اسمك أو المنتج، أو تبي توقف الشراء وتبدأ طلبًا جديدًا؟"
                    : "وش تقصد تعدّل أو تبدأ؟ وضّح لي طلبك";
    }
}
