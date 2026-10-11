using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using RePay.WhatsAppPoc.Services;

var dir = Path.Combine(Path.GetTempPath(), "repay-checks-" + Guid.NewGuid());
var options = Options.Create(new RePayOptions { DataDirectory = dir, MetaAccessToken = "fake", MetaPhoneNumberId = "test", GroqApiKey = "fake", GroqModel = "groq/compound-mini" });
var env = new TestEnvironment { ContentRootPath = dir };
var handler = new RecordingHandler();
var meta = new MetaWhatsAppClient(new HttpClient(handler), options);
var provider = new TestProvider();
var router = new AgentRouter([provider], NullLogger<AgentRouter>.Instance);
var store = new ConversationStore(options, env);
await store.InitializeAsync();
var app = new WhatsAppOrchestrator(meta, router, store);
string Payload(string id, string text, string phone = "966500000001", DateTimeOffset? sentAt = null) => JsonSerializer.Serialize(new { entry = new[] { new { changes = new[] { new { value = new { messages = new[] { new { id, from = phone, timestamp = (sentAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(), text = new { body = text } } } } } } } } });
void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
await app.HandleAsync(Payload("m1", "مرحبا"), default);
Check(handler.Replies.Last().Contains("اسمك") && !handler.Replies.Last().Contains("ري باي المالية"), "Arabic welcome asks for name with no footer");
await app.HandleAsync(Payload("m2", "وش الخدمات؟"), default);
Check(provider.Calls == 1, "Questions during onboarding do not become a name");
await app.HandleAsync(Payload("m3", "اسمي محمد السالم"), default);
Check(handler.Replies.Last().Contains("محمد"), "Name extracted and saved");
var restarted = new ConversationStore(options, env);
await restarted.InitializeAsync();
app = new WhatsAppOrchestrator(meta, router, restarted);
await app.HandleAsync(Payload("m4", "هل تقدر تحول الآن؟"), default);
Check(JsonDocument.Parse(provider.LastInput).RootElement.GetProperty("name").GetString() == "محمد السالم" && !provider.LastInput.Contains("966500000001"), "Restart preserves name without passing phone to AI");
var count = handler.Replies.Count;
await app.HandleAsync(Payload("m4", "هل تقدر تحول الآن؟"), default);
Check(handler.Replies.Count == count, "Duplicate message ignored across persisted state");
await app.HandleAsync(Payload("m5", "1"), default);
Check(provider.Calls == 3, "Numeric reply is normal conversation, never checkout");
await app.HandleAsync(Payload("m6", "hello", "15550000001"), default);
Check(handler.Replies.Last().Contains("السعودية"), "Saudi number restriction preserved");
await new GroqAgentClient(new HttpClient(handler), options).ReplyAsync(provider.LastInput, default);
using var request = JsonDocument.Parse(handler.LastGroqBody!);
Check(request.RootElement.GetProperty("model").GetString() == ServiceConversation.ChatModel && !request.RootElement.TryGetProperty("tools", out _), "Compound override cannot enable search tools");
Check(!ServiceConversation.TryGetName("123456", out _) && !ServiceConversation.TryGetName("مرحبا", out _), "Numbers and greetings rejected as names");
await Task.WhenAll(app.HandleAsync(Payload("m7", "شكرا"), default), app.HandleAsync(Payload("m7", "شكرا"), default));
Check(handler.Replies.Count == count + 3, "Concurrent duplicate serialized");
await app.HandleAsync(Payload("m8", "غير اسمي إلى خالد العلي"), default);
Check(handler.Replies.Last().Contains("خالد"), "Direct rename acknowledged");
await app.HandleAsync(Payload("m9", "ابي اغير اسمي"), default);
Check(handler.Replies.Last().Contains("الثنائي"), "Rename without name asks once");
await app.HandleAsync(Payload("m10", "ناصر العلي"), default);
var renamedStore = new ConversationStore(options, env);
await renamedStore.InitializeAsync();
await using (var saved = await renamedStore.OpenSessionAsync("966500000001", default))
    Check(saved.State.Name == "ناصر العلي" && !saved.State.AwaitingNameChange, "Follow-up rename persisted after restart");
await app.HandleAsync(Payload("m11", "كيف التحويل؟"), default);
var turns = ServiceConversation.ModelTurns(provider.LastInput);
Check(turns.Last().Text == "كيف التحويل؟" && turns.Any(t => t.Role == "assistant"), "Real role history and current question preserved");
Check(!ServiceConversation.IsNameChange("كيف اغير اسم المستفيد؟"), "Recipient name question does not rename user");
var demoState = new ConversationState();
Check(DemoConversation.TryReply(demoState, "جرب تحويل", out var demoReply) && demoReply.Contains("بدون تنفيذ"), "Transfer example starts without financial execution");
DemoConversation.TryReply(demoState, "محمد 0500000000", out _);
Check(DemoConversation.TryReply(demoState, "١٠٠ ريال", out demoReply) && demoReply.Contains("100") && demoReply.Contains("ما تم تحويل"), "Arabic amount creates non-executing summary");
DemoConversation.TryReply(demoState, "جرب شراء", out _);
DemoConversation.TryReply(demoState, "جوال بميزانية 2000", out demoReply);
Check(!demoReply.Contains("الشركات السعودية") && !demoReply.Contains("تخدم السعودية") && demoReply.Contains("ما صار بحث"), "Purchase example has no geographic default and never claims live search");
Check(demoState.TransferExamples == 1 && demoState.SearchExamples == 1 && demoState.PaymentExamples == 1, "Example counters have explicit meanings");
await app.HandleAsync(Payload("demo1", "جرب تحويل"), default);
var contactPayload = JsonSerializer.Serialize(new { entry = new[] { new { changes = new[] { new { value = new { messages = new[] { new { id = "demo2", from = "966500000001", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), contacts = new[] { new { name = new { formatted_name = "خالد" }, phones = new[] { new { phone = "0500000000" } } } } } } } } } } } });
await app.HandleAsync(contactPayload, default);
Check(handler.Replies.Last().Contains("الريال السعودي"), "Shared WhatsApp contact parsed into example recipient");
await app.HandleAsync(Payload("demo3", "250"), default);
Check(handler.Replies[^2].Contains("خالد") && handler.Replies[^2].Contains("250.00 ريال سعودي"), "Shared contact appears in transfer example summary");
await app.HandleAsync(Payload("demo4", "كيف تحمون البيانات؟"), default);
Check(!provider.LastInput.Contains("0500000000"), "Recipient phone excluded from model history");
using var stats = JsonDocument.Parse(JsonSerializer.Serialize(await restarted.AnalyticsAsync(default)));
Check(stats.RootElement.GetProperty("transfers").GetInt64() == 1 && stats.RootElement.GetProperty("users").GetInt64() == 1, "Persistent analytics count example and unique user");
var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
var authResult = await AnalyticsEndpoints.GetAsync(httpContext, options, restarted);
Check(authResult is Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult, "Analytics fail closed when password missing");
handler.FailNext = true;
try { await app.HandleAsync(Payload("retry-demo", "جرب تحويل"), default); } catch (HttpRequestException) { }
await app.HandleAsync(Payload("retry-demo", "جرب تحويل"), default);
await using (var retryState = await restarted.OpenSessionAsync("966500000001", default))
    Check(retryState.State.TransferExamples == 2 && retryState.State.Demo?.Stage == "recipient", "Failed delivery retry reuses summary without double-counting or advancing");
Check(!DemoConversation.TryReply(new ConversationState(), "ما دوركم؟", out _), "Service questions do not start shopping examples");
provider.IntentOutput = _ => "{\"action\":\"rename\",\"value\":null}";
await app.HandleAsync(Payload("name-during-demo", "ممكن تغير الاسم؟"), default);
provider.IntentOutput = null;
await app.HandleAsync(Payload("name-followup", "سلمان العلي"), default);
await app.HandleAsync(Payload("name-read", "وش اسمي؟"), default);
Check(handler.Replies.Last().Contains("*سلمان العلي*"), "Name update takes priority over active demo and is read back");
await app.HandleAsync(Payload("name-attached", "غير اسمي لمحمد السالم"), default);
Check(handler.Replies.Last().Contains("*محمد السالم*"), "Attached Arabic preposition parsed for name update");
await app.HandleAsync(Payload("buy-example", "جرب دفع"), default);
await app.HandleAsync(Payload("buy-product", "سماعات من متجر سعودي"), default);
Check(handler.Replies[^2].Contains("سماعات") && handler.Replies.Last().Contains("تؤكد"), "Product immediately produces summary plus separate confirmation prompt");
await app.HandleAsync(Payload("buy-confirm", "تأكيد"), default);
Check(handler.Replies.Last().Contains("تم تأكيد المثال فقط") && handler.Replies.Last().Contains("طلب جديد"), "Confirm shows summary and resets without payment");
await using (var confirmed = await restarted.OpenSessionAsync("966500000001", default))
    Check(confirmed.State.Demo is null, "Confirmation clears persisted demo state");
using var maskedStats = JsonDocument.Parse(JsonSerializer.Serialize(await restarted.AnalyticsAsync(default)));
var userRow = maskedStats.RootElement.GetProperty("userList")[0];
Check(userRow.GetProperty("mobile").GetString() == "050 XXXX 0001" && userRow.GetProperty("name").GetString() == "محمد السالم" && !maskedStats.RootElement.ToString().Contains("966500000001"), "Analytics exposes name and masked local phone");
await app.HandleAsync(Payload("follow-retry-start", "جرب شراء"), default);
handler.FailAfter = 1;
try { await app.HandleAsync(Payload("follow-retry-product", "جوال"), default); } catch (HttpRequestException) { }
var beforeRetry = handler.Replies.Count;
await app.HandleAsync(Payload("follow-retry-product", "جوال"), default);
Check(handler.Replies.Count == beforeRetry + 1 && handler.Replies.Last().Contains("تؤكد"), "Follow-up retry does not resend delivered summary");
var currencyState = new ConversationState { Demo = new DemoState { Kind = "transfer", Stage = "amount", Recipient = "خالد" } };
DemoConversation.TryReply(currencyState, "100 دولار", out var currencyReply);
Check(currencyState.Demo.Stage == "amount" && currencyReply.Contains("السعودي فقط"), "Non-SAR amount rejected rather than relabelled");
var switching = new ConversationState { Name = "محمد السالم" };
DemoConversation.TryReply(switching, "جرب تحويل", out _);
DemoConversation.TryReply(switching, "خالد العلي", out _);
DemoConversation.TryReply(switching, "ابي اشتري جوال", out var switchedReply);
Check(switching.Demo?.Kind == "payment" && switchedReply.Contains("جوال"), "Switch from transfer amount to purchase preserves product");
DemoConversation.TryReply(switching, "ابي احول", out _);
Check(switching.Demo?.Kind == "transfer" && switching.Demo.Stage == "recipient", "Switch from purchase to transfer clears previous details");
NameConversation.TryReply(switching, "ابغى اغير اسمي", out _);
NameConversation.TryReply(switching, "محمد", out var shortNameReply);
Check(switching.AwaitingNameChange && shortNameReply.Contains("الثنائي"), "Single name rejected while retaining name edit flow");
NameConversation.TryReply(switching, "محمد العلي", out _);
Check(switching.Name == "محمد العلي" && !switching.AwaitingNameChange && switching.Demo is null, "Full name saved while cancelling stale demo");
var instant = DateTimeOffset.UtcNow;
switching.LastMessageAt = instant.AddMinutes(-5);
switching.Demo = new DemoState { Kind = "transfer", Stage = "amount" };
switching.Turns.Add(new ChatTurn("user", "old context"));
Check(ConversationLifecycle.Expire(switching, instant) && switching.Demo is null && switching.Turns.Count == 0 && switching.Name == "محمد العلي", "Five-minute reset clears conversation but preserves identity");
Check(ConversationLifecycle.Format("مثال.\n*100.00 ريال سعودي*\nhttps://repay.sa") == "مثال\n*100.00 ريال سعودي*\nhttps://repay.sa", "Formatting removes terminal periods without altering amounts or URLs");
Check(demoState.Demo?.Summary?.Contains("حساب ري باي") == true, "Summary includes illustrative source account");

Check(ConversationLifecycle.MaskPhone("٩٦٦٥٤١٠٠٩٤٤٩") == "054 XXXX 9449", "Arabic phone masking");
Check(ConversationLifecycle.Format("١٢٣ ۴۵۶") == "123 456", "All outgoing digits use Latin numerals");
var phoneOptions = Microsoft.Extensions.Options.Options.Create(new RePayOptions { AnalyticsPhone = "0500000001" });
httpContext.Request.Headers.Authorization = "Bearer ٠٥٠٠٠٠٠٠٠١";
Check(await AnalyticsEndpoints.GetAsync(httpContext, phoneOptions, restarted) is not Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult, "Phone login accepts Arabic digits");
httpContext.Request.Headers.Authorization = "Bearer 0500000002";
Check(await AnalyticsEndpoints.GetAsync(httpContext, phoneOptions, restarted) is Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult, "Phone login rejects other numbers");

foreach (var casual in new[] { "هلا حبيبي", "هلا والله", "السلام عليكم", "صباح الخير", "كيف حالك", "ابي احول", "ما عندي", "شكرا لك", "hello there", "ادور جوال" })
    Check(!ServiceConversation.TryGetName(casual, out _) && !NameConversation.TryFullName(casual, out _), "Conversation is not a name: " + casual);
Check(ServiceConversation.TryGetName("محمد", out _) && !NameConversation.TryFullName("محمد", out _), "First name is recognized but needs family name");
Check(NameConversation.TryFullName("محمد العلي", out _), "Full name remains supported");
var pendingRename = new ConversationState { Name = "محمد العلي", AwaitingNameChange = true, Welcomed = true };
NameConversation.TryReply(pendingRename, "هلا حبيبي", out var greetingReply);
Check(pendingRename.Name == "محمد العلي" && pendingRename.AwaitingNameChange && greetingReply.Contains("يا هلا"), "Greeting during rename preserves saved identity and pending name request");
NameConversation.TryReply(pendingRename, "اسمي هلا حبيبي", out _);
Check(pendingRename.Name == "محمد العلي", "Explicit prefix cannot turn greeting into saved name");
// Redelivery after the deduplication window must not revive old conversations.
var repliesBeforeStale = handler.Replies.Count;
var callsBeforeStale = provider.Calls;
await app.HandleAsync(Payload("old-unseen", "مرحبا", sentAt: DateTimeOffset.UtcNow.AddMinutes(-6)), default);
await app.HandleAsync(Payload("future-unseen", "مرحبا", sentAt: DateTimeOffset.UtcNow.AddMinutes(2)), default);
await app.HandleAsync(Payload("old-foreign", "hello", "15550000001", DateTimeOffset.UtcNow.AddMinutes(-6)), default);
await app.HandleAsync(Payload("no-timestamp", "مرحبا").Replace("\"timestamp\"", "\"missingTimestamp\""), default);
await app.HandleAsync("{\"entry\":[{\"changes\":[{\"value\":{\"statuses\":[{\"id\":\"status-only\"}]}}]}]}", default);
Check(handler.Replies.Count == repliesBeforeStale && provider.Calls == callsBeforeStale, "Old, future, timestamp-free and status events do not reply");
await using (var legacy = await restarted.OpenSessionAsync("966500000002", default))
{
    legacy.State.Name = "محمد";
    legacy.State.Welcomed = true;
    legacy.State.AwaitingNameChange = true;
    legacy.State.Turns.Add(new ChatTurn("assistant", "عشان نحدّث بياناتك، ممكن اسمك الثنائي؟ الاسم الأول واسم العائلة"));
    await legacy.SaveAsync(default);
}
await app.HandleAsync(Payload("legacy-question", "وش الخدمات؟", "966500000002"), default);
Check(provider.Calls == callsBeforeStale + 1 && !handler.Replies.Last().Contains("نحدّث بياناتك"), "Legacy first name does not force repeated profile upgrade");
await using (var legacy = await restarted.OpenSessionAsync("966500000002", default))
    Check(legacy.State.Name == "محمد" && !legacy.State.AwaitingNameChange, "Legacy name retained and automatic upgrade request cleared");

// A webhook disconnection after a successful send must not cancel the persisted checkpoint.
using var disconnect = new CancellationTokenSource();
var webhookOptions = Options.Create(new RePayOptions { MetaAppSecret = "test-secret" });
var webhookContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
var webhookBody = Payload("disconnect-check", "هلا", "966500000002");
webhookContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(webhookBody));
webhookContext.Request.Headers["X-Hub-Signature-256"] = "sha256=" + Convert.ToHexStringLower(
    System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes("test-secret"), System.Text.Encoding.UTF8.GetBytes(webhookBody)));
webhookContext.RequestAborted = disconnect.Token;
handler.OnSent = () => disconnect.Cancel();
var webhookResult = await WebhookEndpoints.ReceiveAsync(webhookContext.Request, webhookOptions, app, NullLoggerFactory.Instance, new TestLifetime());
handler.OnSent = null;
Check(webhookResult is Microsoft.AspNetCore.Http.HttpResults.Ok && disconnect.IsCancellationRequested, "Client disconnect does not cancel webhook processing");
var beforeDisconnectedRetry = handler.Replies.Count;
await app.HandleAsync(webhookBody, default);
Check(handler.Replies.Count == beforeDisconnectedRetry, "Disconnected webhook retry does not resend saved reply");
async Task<string> Route(ConversationState state, string input, string action, string? value = null)
{
    provider.IntentOutput = _ => JsonSerializer.Serialize(new { action, value });
    try { return await ContextConversation.ReplyAsync(state, input, router, default); }
    finally { provider.IntentOutput = null; }
}
var contextual = new ConversationState { Name = "محمد العلي", Welcomed = true,
    Demo = new DemoState { Kind = "transfer", Stage = "amount", Recipient = "خالد العلي" } };
var ownName = await Route(contextual, "وش اسمي؟", "question");
contextual.Turns.Add(new ChatTurn("user", "وش اسمي؟"));
contextual.Turns.Add(new ChatTurn("assistant", ownName));
var editOwnName = await Route(contextual, "ابي اعدله", "rename");
Check(contextual.Demo is null && contextual.AwaitingNameChange && editOwnName.Contains("اسمك"), "Contextual own-name edit cancels transfer instead of asking amount");
using (var intentContext = JsonDocument.Parse(provider.LastIntent))
{
    Check(intentContext.RootElement.GetProperty("conversationState").GetProperty("focus").GetString() == "profile"
        && intentContext.RootElement.GetProperty("recentConversation")[1].GetProperty("Text").GetString()!.Contains("محمد العلي"), "Interpreter receives profile focus and actual role history");
}
await Route(contextual, "ناصر السالم", "continue");
Check(contextual.Name == "ناصر السالم" && !contextual.AwaitingNameChange, "Contextual rename saves only a validated full name");
await Route(contextual, "جرب تحويل", "start_transfer");
await Route(contextual, "خالد العلي", "continue");
await Route(contextual, "100 ريال", "continue");
Check(contextual.Demo?.Stage == "confirm", "Transfer summary ready before edits");
await Route(contextual, "غير المبلغ إلى 200", "edit_amount", "200");
Check(contextual.Demo?.Amount == 200 && contextual.Demo.Summary!.Contains("200.00")
    && !contextual.Demo.Summary.Contains("100.00") && contextual.Demo.Stage == "confirm", "Amount edit replaces stale summary and requires fresh confirmation");
await Route(contextual, "غير المستفيد إلى سلمان العلي", "edit_recipient", "سلمان العلي");
Check(contextual.Demo?.Recipient == "سلمان العلي" && contextual.Demo.Summary!.Contains("سلمان العلي")
    && !contextual.Demo.Summary.Contains("خالد العلي") && contextual.Demo.Amount == 200, "Recipient edit preserves amount and rebuilds summary");
await Route(contextual, "لا ابي ادفع بدل التحويل", "start_payment");
Check(contextual.Demo?.Kind == "payment" && contextual.Demo.Stage == "product" && contextual.Demo.Recipient is null
    && !contextual.AwaitingNameChange, "Negative transfer phrase switches to payment with old transfer details cleared");
await Route(contextual, "سماعات", "continue", "سماعات");
await Route(contextual, "بدل السماعات ابي جوال", "edit_product", "جوال");
Check(contextual.Demo?.Product == "جوال" && contextual.Demo.Summary!.Contains("جوال")
    && !contextual.Demo.Summary.Contains("سماعات") && !contextual.Demo.Summary.Contains("تخدم السعودية"), "Product edits replace old item without geographic wording");
await Route(contextual, "ابي احول", "start_transfer");
Check(contextual.Demo?.Kind == "transfer" && contextual.Demo.Product is null, "Payment-to-transfer clears product and confirmation");
await Route(contextual, "ابي اعدله", "clarify");
Check(contextual.Demo?.Stage == "recipient" && contextual.Demo.Recipient is null && contextual.AwaitingClarification, "Ambiguous edit clarifies without inventing a recipient");
await Route(contextual, "اسمي", "rename");
Check(contextual.Demo is null && contextual.AwaitingNameChange, "Clarification reply 'my name' selects profile editing instead of reading the name");
await Route(contextual, "ابي ادفع", "start_payment");
Check(!contextual.AwaitingNameChange && contextual.Demo?.Kind == "payment", "New payment request interrupts pending rename");
await Route(contextual, "وقف خلنا نتكلم عن الخدمات", "new_topic");
Check(contextual.Demo is null && !contextual.AwaitingNameChange, "New topic discards old task instead of resuming payment");
await Route(contextual, "تمام", "continue");
Check(contextual.Demo is null, "Acknowledgment after new topic does not confirm an abandoned request");
await Route(contextual, "جرب تحويل", "start_transfer");
await Route(contextual, "ما ابي احول خلاص", "cancel");
Check(contextual.Demo is null, "Negated transfer request cancels rather than starting transfer");
await Route(contextual, "جرب تحويل", "start_transfer");
await Route(contextual, "ابي اعدله", "continue");
Check(contextual.Demo is { Recipient: null, Stage: "recipient" }, "Even an incorrect continue decision cannot consume an edit phrase as recipient");
await Route(contextual, "محمد السالم", "rename", "خالد المزيف");
Check(contextual.Name == "ناصر السالم" && contextual.Demo is not null, "Invented model slot rejected without mutating identity or task");
await Route(contextual, "اسمي", "rename");
await Route(contextual, "ناصر السالم", "continue");
await Route(contextual, "جرب تحويل", "start_transfer");
await Route(contextual, "خالد العلي", "continue");
await Route(contextual, "100", "continue");
contextual.PendingFollowUp = null;
contextual.Turns.Add(new ChatTurn("assistant", "الفكرة إن كل عملية تحتاج موافقتك"));
await Route(contextual, "تمام", "continue");
Check(contextual.Demo?.Stage == "confirm" && contextual.AwaitingClarification, "Acknowledgment of side answer does not accidentally confirm transfer");
Check(ConversationIntent.Parse("{\"action\":\"send_money\",\"value\":null}") is null
    && ConversationIntent.Parse("not json") is null, "Intent parser rejects unrecognized actions and invalid JSON");
provider.IntentOutput = _ => "invalid JSON";
var unavailable = new ConversationState { Name = "محمد العلي", Welcomed = true, Demo = new DemoState { Kind = "payment", Stage = "product" } };
await ContextConversation.ReplyAsync(unavailable, "ابي اعدله", router, default);
provider.IntentOutput = null;
Check(unavailable.AwaitingClarification && unavailable.Demo?.Product is null, "Invalid provider output falls back to clarification without advancing");
await using (var persistent = await restarted.OpenSessionAsync("966500000003", default))
{
    persistent.State.Name = "محمد العلي";
    persistent.State.Welcomed = true;
    persistent.State.Focus = "profile";
    persistent.State.Demo = new DemoState { Kind = "transfer", Stage = "amount", Recipient = "خالد 0500000000" };
    persistent.State.Turns.Add(new ChatTurn("assistant", "اسمك المحفوظ محمد العلي"));
    await persistent.SaveAsync(default);
}
provider.IntentOutput = _ => "{\"action\":\"rename\",\"value\":null}";
handler.FailNext = true;
try { await app.HandleAsync(Payload("context-retry", "ابي اعدله", "966500000003"), default); } catch (HttpRequestException) { }
provider.IntentOutput = _ => throw new Exception("Must not reinterpret persisted reply");
await app.HandleAsync(Payload("context-retry", "ابي اعدله", "966500000003"), default);
provider.IntentOutput = null;
var contextRestart = new ConversationStore(options, env);
await using (var persistedContext = await contextRestart.OpenSessionAsync("966500000003", default))
    Check(persistedContext.State.Focus == "profile" && persistedContext.State.AwaitingNameChange && persistedContext.State.Demo is null, "Context transition persists across failed delivery, retry and store restart");
Check(!provider.LastIntent.Contains("0500000000") && !provider.LastIntent.Contains("966500000003"), "Intent routing receives redacted recipient and sender phone numbers");
handler.GroqContent = "{\"action\":\"rename\",\"value\":null}";
var groqIntent = await new GroqAgentClient(new HttpClient(handler), options).InterpretAsync(ContextConversation.Context(contextual, "ابي اعدله"), default);
using (var sentIntent = JsonDocument.Parse(handler.LastGroqBody!))
    Check(ConversationIntent.Parse(groqIntent)?.Action == "rename"
        && sentIntent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("edit_recipient")
        && !sentIntent.RootElement.TryGetProperty("tools", out _), "Groq sends dedicated intent instructions and parses transport response without tools");
handler.GroqContent = null;
var fallbackRouter = new AgentRouter([new FixedIntentProvider("invalid JSON"), new FixedIntentProvider("{\"action\":\"rename\",\"value\":null}")], NullLogger<AgentRouter>.Instance);
Check((await fallbackRouter.InterpretAsync(ContextConversation.Context(contextual, "ابي اعدله"), default)).Action == "rename", "Invalid first-provider intent tries configured fallback");
var recipientEdit = new ConversationState { Name = "محمد العلي", Welcomed = true, Demo = new DemoState { Kind = "transfer", Stage = "confirm", Recipient = "خالد العلي", Amount = 100 } };
await Route(recipientEdit, "غير الاسم حق المستفيد", "edit_recipient");
Check(recipientEdit.Name == "محمد العلي" && !recipientEdit.AwaitingNameChange && recipientEdit.Demo?.Stage == "recipient", "Beneficiary name edit is never mistaken for profile name edit");
await Route(recipientEdit, "غيرت رايي", "clarify");
Check(recipientEdit.Demo?.Recipient == "خالد العلي" && recipientEdit.AwaitingClarification, "Change-of-mind phrase never becomes a beneficiary name");
var pendingProfile = new ConversationState { Name = "محمد العلي", Welcomed = true, AwaitingNameChange = true };
await Route(pendingProfile, "غيرت رايي ابي ادفع", "start_payment");
Check(!pendingProfile.AwaitingNameChange && pendingProfile.Name == "محمد العلي" && pendingProfile.Demo?.Kind == "payment", "Natural change-of-mind interrupts profile collection without overwriting name");
var purchase = new ConversationState { Name = "محمد العلي", Welcomed = true };
await Route(purchase, "ابي اشتري جوال", "start_payment");
Check(purchase.Demo?.Amount == 2000 && purchase.Demo.Budget is null && purchase.Demo.Stage == "confirm"
    && !purchase.Demo.Summary!.Contains("كم") && !purchase.Demo.Summary.Contains("ميزانيتك"), "Product-only purchase chooses illustrative price without asking an amount");
await Route(purchase, "بميزانية ٢٠٠٠ ريال", "edit_budget");
Check(purchase.Demo?.Budget == 2000 && purchase.Demo.Amount == 2000 && purchase.Demo.Summary!.Contains("يطابق"), "Explicit Arabic budget chooses exact synthetic option");
await Route(purchase, "بمبلغ ٢١٠٠ ريال", "edit_budget");
Check(purchase.Demo?.Amount == 2000 && purchase.Demo.Summary!.Contains("100.00") && purchase.Demo.Summary.Contains("أقل من ميزانيتك"), "Missing exact price selects nearest option and states difference");
await Route(purchase, "بميزانية 1700", "edit_budget");
Check(purchase.Demo?.Amount == 1800 && purchase.Demo.Summary!.Contains("أعلى من ميزانيتك") && purchase.PendingFollowUp is not null, "Nearby option above target is disclosed and offered for confirmation");
await Route(purchase, "حد اقصى 1700", "edit_budget");
Check(purchase.Demo?.Amount is null && purchase.Demo?.Summary is null && purchase.PendingFollowUp is null
    && purchase.Demo?.Stage == "product", "Hard ceiling cannot silently exceed budget or retain old confirmation");
await Route(purchase, "تأكيد", "continue");
Check(purchase.Demo?.Stage == "product" && purchase.Demo.Summary is null, "Unmatched budget cannot confirm stale purchase summary");
await Route(purchase, "بدون ميزانية محددة", "clear_budget");
Check(purchase.Demo?.Budget is null && purchase.Demo?.Amount == 2000, "Removing optional budget restores automatic example price");
await Route(purchase, "ابي احول", "start_transfer");
Check(purchase.Demo?.Budget is null && purchase.Demo?.Amount is null && purchase.Demo?.Stage == "recipient", "Switching to transfer discards purchase budget and price");
var budgetFirst = new ConversationState { Welcomed = true, Name = "محمد العلي" };
await Route(budgetFirst, "ابي اشتري بميزانية 2000", "start_payment");
Check(budgetFirst.Demo?.Product is null && budgetFirst.Demo?.Budget == 2000 && budgetFirst.Demo?.Stage == "product", "Budget without product asks only for product and does not treat amount as item");
await Route(budgetFirst, "جوال", "continue", "جوال");
Check(budgetFirst.Demo?.Amount == 2000 && budgetFirst.Demo?.Budget == 2000, "Budget-first purchase retains constraint for subsequent product");
Check(!PurchaseExample.TryBudget("ايفون 16", out _, out _, out _) && PurchaseExample.Select("ايفون 16", null, false) == 2000, "Model number is not mistaken for a budget");
await using (var savedBudget = await restarted.OpenSessionAsync("966500000004", default))
{
    savedBudget.State.Demo = budgetFirst.Demo;
    await savedBudget.SaveAsync(default);
}
await using (var savedBudget = await new ConversationStore(options, env).OpenSessionAsync("966500000004", default))
    Check(savedBudget.State.Demo?.Budget == 2000 && savedBudget.State.Demo.Amount == 2000, "Optional budget and selected example price survive store restart");
Console.WriteLine("All conversation checks passed.");

sealed class TestProvider : IAgentProvider
{
 public string LastIntent = ""; public Func<string, string>? IntentOutput;
 public Task<string> InterpretAsync(string text, CancellationToken ct)
 {
  LastIntent = text;
  using var json = JsonDocument.Parse(text);
  var current = json.RootElement.GetProperty("currentMessage").GetString()!;
  return Task.FromResult(IntentOutput?.Invoke(current) ?? JsonSerializer.Serialize(new { action = System.Text.RegularExpressions.Regex.IsMatch(current, @"^(كيف|وش|ايش|هل|متى|ليش)\b") ? "question" : "continue", value = current }));
 }
 public string Name => "Test"; public bool IsConfigured => true; public int Calls; public string LastInput = "";
 public Task<string> ReplyAsync(string text, CancellationToken ct) { Calls++; LastInput = text; return Task.FromResult("الخدمات بتتاح تدريجيًا، وحاليًا أجاوب عن أسئلتك عنها."); }
}
sealed class RecordingHandler : HttpMessageHandler
{
 public Action? OnSent; public List<string> Replies = []; public string? LastGroqBody; public string? GroqContent; public bool FailNext; public int FailAfter = -1;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
 {
  var body = await request.Content!.ReadAsStringAsync(ct);
  if (request.RequestUri!.Host == "api.groq.com") { LastGroqBody = body; return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = GroqContent ?? "الخدمات قريبًا" } } } })) }; }
  if (request.RequestUri.Host != "graph.facebook.com") throw new Exception("Unexpected browser/network call");
  if (FailAfter == 0) { FailAfter = -1; return new(HttpStatusCode.ServiceUnavailable); }
  if (FailAfter > 0) FailAfter--;
  if (FailNext) { FailNext = false; return new(HttpStatusCode.ServiceUnavailable); }
  using var json = JsonDocument.Parse(body); Replies.Add(json.RootElement.GetProperty("text").GetProperty("body").GetString()!);
  OnSent?.Invoke();
  return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
 }
}
sealed class TestEnvironment : IHostEnvironment
{
 public string EnvironmentName { get; set; } = "Testing"; public string ApplicationName { get; set; } = "Tests";
 public string ContentRootPath { get; set; } = ""; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

sealed class TestLifetime : IHostApplicationLifetime
{
 public CancellationToken ApplicationStarted => CancellationToken.None;
 public CancellationToken ApplicationStopping => CancellationToken.None;
 public CancellationToken ApplicationStopped => CancellationToken.None;
 public void StopApplication() { }
}
sealed class FixedIntentProvider(string output) : IAgentProvider
{
 public string Name => "Fixed"; public bool IsConfigured => true;
 public Task<string> InterpretAsync(string context, CancellationToken ct) => Task.FromResult(output);
 public Task<string> ReplyAsync(string text, CancellationToken ct) => throw new NotSupportedException();
}
