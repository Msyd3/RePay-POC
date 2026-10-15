using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using RePay.WhatsAppPoc.Services;

void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
var provider = new TestProvider();
var router = new AgentRouter([provider], NullLogger<AgentRouter>.Instance);
Task<string> Reply(ConversationState s, string text) => TransferConversation.ReplyAsync(s, text, router, default);
var s = new ConversationState();
var reply = await Reply(s, "حول لمحمد ٢٠ ريال");
Check(s.Transfer?.RecipientName == "محمد" && s.Transfer.Amount == 20 && reply.Contains("الثنائي"), "One Arabic request preserves recipient and amount, asks sender name first");
reply = await Reply(s, "٠٥٠٠٠٠٠٠٠٠");
Check(s.Transfer?.RecipientPhone == "966500000000" && reply.Contains("الثنائي"), "Arabic mobile normalized, sender name required before review");
reply = await Reply(s, "محمد السالم");
Check(s.Name == "محمد السالم" && s.PendingReviewButton is not null && reply.Contains("20.00"), "Sender full name completes review");
var oldButton = s.PendingReviewButton;
reply = await Reply(s, "غير اسمي إلى خالد العلي");
Check(s.Name == "خالد العلي" && s.Transfer?.Amount == 20 && s.PendingReviewButton != oldButton, "Rename preserves draft and invalidates old review");
reply = await Reply(s, "review:" + oldButton);
Check(reply.Contains("تغيّرت") && s.PendingReviewButton is null, "Old button cannot review changed details");
reply = await Reply(s, "كمل");
var currentButton = s.PendingReviewButton;
reply = await Reply(s, "review:" + currentButton);
Check(reply.Contains("لاحقًا") && s.Transfer is not null, "Review button explicitly waits for future page, no execution");
s.Transfer!.ReviewExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
Check((await Reply(s, "review:" + currentButton)).Contains("انتهى"), "Review expires after three minutes");
reply = await Reply(s, "غير المبلغ إلى ٣٠ ريال");
Check(s.Transfer?.Amount == 30 && s.PendingReviewButton is not null, "Amount edit produces fresh review");
reply = await Reply(s, "حول لعلي 50 ريال");
Check(s.Transfer?.RecipientName == "علي" && s.Transfer.RecipientPhone is null && s.Transfer.Amount == 50, "Different named recipient cannot inherit previous phone");
await Reply(s, "لا تحول");
Check(s.Transfer is null, "Negated transfer cancels");
var en = new ConversationState { Name = "John Smith" };
reply = await Reply(en, "send 20 SAR to Mohammed");
Check(en.Transfer?.RecipientName == "Mohammed" && en.Transfer.Amount == 20 && reply.Contains("mobile"), "English amount-first transfer");
reply = await Reply(en, "0500000000");
Check(en.Language == "en" && reply.Contains("Operation summary") && !reply.Contains("المبلغ"), "Numeric continuation retains English");
reply = await Reply(en, "change my name to Jane Doe");
Check(en.Name == "Jane Doe" && en.Transfer?.Amount == 20, "English account rename preserves transfer");
reply = await Reply(en, "ابي اشتري جوال");
Check(reply.Contains("للتحويل فقط") && en.Language == "ar", "Shopping removed and current message controls language");
var contact = new ConversationState { Name = "محمد السالم" };
reply = await Reply(contact, "contact:Ahmed Saleh +966 50 000 0000");
Check(contact.Transfer?.RecipientName == "Ahmed Saleh" && contact.Transfer.RecipientPhone == "966500000000" && contact.Language == "ar", "Contact card preserves names and mobile without changing chat language");
reply = await Reply(contact, "حوله ٢٠");
Check(contact.Transfer?.Amount == 20 && contact.Transfer.RecipientName == "Ahmed Saleh" && contact.PendingReviewButton is not null, "Shared contact followed by pronoun and amount");
provider.Action = "rename";
reply = await Reply(contact, "قصدي على اسمي");
Check(contact.AwaitingNameChange && contact.Transfer?.Amount == 20 && contact.Transfer.ReviewId is null, "Contextual account edit never asks amount");
await Reply(contact, "ناصر العلي");
Check(contact.Name == "ناصر العلي" && contact.PendingReviewButton is not null, "Contextual rename resumes retained draft");
provider.Action = "question";
var before = contact.Transfer!.Amount;
await Reply(contact, "كم رسوم التحويل؟");
Check(contact.Transfer.Amount == before, "Question cannot mutate transfer");
Check(!NameConversation.TryFullName("hello friend", out _) && !NameConversation.TryFullName("محمد", out _), "Greeting and first-only sender names rejected");
var noteState = new ConversationState { Name = "John Smith" };
await Reply(noteState, "transfer 20 SAR to Mohammed note: dinner");
Check(noteState.Transfer?.Note == "dinner" && noteState.Transfer.RecipientName == "Mohammed", "Optional note extracted without corrupting recipient");
var dir = Path.Combine(Path.GetTempPath(), "repay-checks-" + Guid.NewGuid());
var options = Options.Create(new RePayOptions { DataDirectory = dir, MetaAccessToken = "fake", MetaPhoneNumberId = "test" });
var env = new TestEnvironment { ContentRootPath = dir };
var handler = new RecordingHandler();
var store = new ConversationStore(options, env); await store.InitializeAsync();
var app = new WhatsAppOrchestrator(new MetaWhatsAppClient(new HttpClient(handler), options), router, store);
string Payload(string id, object body, DateTimeOffset? sentAt = null) => JsonSerializer.Serialize(new { entry = new[] { new { changes = new[] { new { value = new { metadata = new { phone_number_id = "test" }, messages = new[] { new { id, from = "966500000001", timestamp = (sentAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(), text = body } } } } } } } });
await app.HandleAsync(Payload("a", new { body = "send Mohammed 20 SAR" }), default);
await app.HandleAsync(Payload("b", new { body = "0500000000" }), default);
await app.HandleAsync(Payload("c", new { body = "John Smith" }), default);
using (var json = JsonDocument.Parse(handler.Bodies.Last()))
{
 var root = json.RootElement;
 Check(root.GetProperty("type").GetString() == "interactive" && root.GetProperty("interactive").GetProperty("action").GetProperty("parameters").GetProperty("display_text").GetString() == "Confirm operation", "Outbound Meta CTA has confirmation label");
}
var count = handler.Bodies.Count;
await app.HandleAsync(Payload("wrong-sender", new { body = "hello" }).Replace("\"phone_number_id\":\"test\"", "\"phone_number_id\":\"other-number\""), default);
Check(handler.Bodies.Count == count, "Other business/test number cannot trigger replies from configured sender");
await app.HandleAsync(Payload("c", new { body = "John Smith" }), default);
await app.HandleAsync(Payload("stale", new { body = "hello" }, DateTimeOffset.UtcNow.AddMinutes(-10)), default);
Check(handler.Bodies.Count == count, "Duplicate and stale webhooks never reply");
await using (var saved = await new ConversationStore(options, env).OpenSessionAsync("966500000001", default))
 Check(saved.State.Name == "John Smith" && saved.State.Transfer?.Amount == 20 && saved.State.Language == "en", "Name, transfer, language survive restart");
var cardPayload = JsonSerializer.Serialize(new { entry = new[] { new { changes = new[] { new { value = new { metadata = new { phone_number_id = "test" }, messages = new[] { new { id = "card", from = "966500000002", timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), contacts = new[] { new { name = new { formatted_name = "خالد" }, phones = new[] { new { phone = "0500000000" } } } } } } } } } } } });
await app.HandleAsync(cardPayload, default);
await using (var saved = await store.OpenSessionAsync("966500000002", default)) Check(saved.State.Transfer?.RecipientName == "خالد" && saved.State.Transfer.RecipientPhone == "966500000000", "Actual contact webhook creates recipient draft");
var failing = new ConversationState { Name = "John Smith" };
await using (var saved = await store.OpenSessionAsync("966500000003", default)) { saved.State.Name = failing.Name; await saved.SaveAsync(default); }
string retryPayload = Payload("retry", new { body = "send Mohammed 20 SAR 0500000000" }).Replace("966500000001", "966500000003");
handler.FailNext = true;
try { await app.HandleAsync(retryPayload, default); } catch (HttpRequestException) { }
await app.HandleAsync(retryPayload, default);
Check(handler.Bodies[^1] == handler.Bodies[^2], "Failed button send retries identical persisted payload");
var phoneFirst = new ConversationState { Name = "John Smith" };
await Reply(phoneFirst, "0500000000");
Check(phoneFirst.Transfer?.RecipientPhone == "966500000000", "Mobile-only message starts transfer draft");
await Reply(phoneFirst, "محمد");
await Reply(phoneFirst, "20");
phoneFirst.Name = null;
phoneFirst.Transfer!.RequestedField = "sender";
await Reply(phoneFirst, "خالد");
Check(phoneFirst.Name is null && phoneFirst.Transfer?.RecipientName == "محمد", "First-only sender answer cannot overwrite recipient");
var reviewNode = System.Text.Json.Nodes.JsonNode.Parse(Payload("button", new { body = "unused" }))!;
var reviewMessage = reviewNode["entry"]![0]!["changes"]![0]!["value"]!["messages"]![0]!.AsObject();
reviewMessage.Remove("text");
reviewMessage["interactive"] = System.Text.Json.Nodes.JsonNode.Parse("""{"button_reply":{"id":"review:expired","title":"Review transfer"}}""");
var reviewPayload = reviewNode.ToJsonString();
await app.HandleAsync(reviewPayload, default);
Check(handler.Bodies.Last().Contains("expired"), "Actual interactive reply webhook reaches review validation");
var invalid = new ConversationState { Name = "John Smith" };
await Reply(invalid, "send Mohammed -20 SAR");
Check(invalid.Transfer is null, "Negative amount cannot become positive transfer");
await Reply(invalid, "send Mohammed 0 SAR");
Check(invalid.Transfer is null, "Zero amount rejected before creating review");
await Reply(invalid, "رقم جوال 0500000000");
Check(invalid.Transfer is { RecipientName: null } && invalid.Transfer.RecipientPhone == "966500000000", "Phone label cannot become recipient name");
var welcome = new ConversationState();
reply = await Reply(welcome, "مرحبا");
Check(reply.Contains("الثنائي") && !reply.Contains("للتحويل"), "Unknown customer greeted and asked for full name first");
reply = await Reply(welcome, "محمد السالم");
Check(reply.Contains("يا هلا محمد السالم") && reply.Contains("\n\nللتحويل السريع") && welcome.Name == "محمد السالم", "Saved name produces personalized welcome on separate line");
reply = await Reply(welcome, "hello");
Check(reply.Contains("Hi محمد السالم") && reply.Contains("To send money quickly"), "Known customer greeted in current message language");
reply = await Reply(welcome, "وش هي ري باي؟");
Check(reply.Contains("الموقع الرسمي: https://repay.sa") && welcome.Language == "ar", "Arabic RePay definition is exact requested copy");
reply = await Reply(welcome, "what is RePay?");
Check(reply.StartsWith("For information about RePay") && welcome.Language == "en", "English RePay question gets English definition");
await Reply(welcome, "contact:محمد العلي ٠٥١٢٣٤٥٦٧٨");
await Reply(welcome, "الغاء");
reply = await Reply(welcome, "حول لمحمد ٢٠ ريال");
Check(welcome.Transfer?.RecipientPhone == "966512345678" && welcome.Transfer.Amount == 20 && reply.Contains("ملخص العملية"), "Name resolves a previously shared contact and Arabic amount");
Check(reply.Contains("\u20660512345678\u2069"), "Full mobile is isolated left to right with English digits");
await Reply(welcome, "الغاء");
await Reply(welcome, "محمد");
Check(welcome.Transfer?.RecipientPhone == "966512345678", "Standalone recipient name resolves shared contact");
await Reply(welcome, "الغاء");
await Reply(welcome, "contact:محمد الثاني 0500000001");
await Reply(welcome, "الغاء");
reply = await Reply(welcome, "حول لمحمد 20 ريال");
Check(welcome.Transfer?.RecipientPhone is null && reply.Contains("رقم جوال"), "Ambiguous contact names never select someone silently");
var bilingual = new ConversationState { Name = "John Smith" };
reply = await Reply(bilingual, "حول John Smith ٢٠ ريال");
Check(bilingual.Language == "ar" && reply.Contains("رقم"), "Arabic request with English name stays Arabic");
var fresh = new ConversationState { Name = "محمد السالم" };
await Reply(fresh, "contact:محمد العلي 0500000000");
await Reply(fresh, "20");
var previousReview = fresh.Transfer!.ReviewId;
reply = await Reply(fresh, "0500000000");
Check(fresh.Transfer is { Amount: null } && fresh.Transfer.RecipientName == "محمد العلي" && fresh.Transfer.ReviewId is null && reply.Contains("كم"), "Same phone starts new send without reusing old amount");
await Reply(fresh, "35");
reply = await Reply(fresh, "عدل المبلغ");
Check(fresh.Transfer is { Amount: null } && fresh.Transfer.RequestedField == "amount" && reply.Contains("كم") && !reply.Contains("تقصد"), "Arabic edit amount directly asks new amount without model");
await Reply(fresh, "50");
reply = await Reply(fresh, "change the amount");
Check(fresh.Transfer?.Amount is null && reply.Contains("How much") && !reply.Contains("Do you mean"), "English edit amount directly asks new amount");
await Reply(fresh, "60");
reply = await Reply(fresh, "حول لمحمد");
Check(fresh.Transfer?.Amount is null && reply.Contains("كم"), "Fresh named send never inherits completed amount");
await Reply(fresh, "حوله ٢٠");
Check(fresh.Transfer?.Amount == 20 && fresh.Transfer.RecipientPhone == "966500000000", "Pronoun while waiting for amount retains recipient");
reply = await Reply(fresh, "عدل المبلغ إلى ٧٠ ريال");
Check(fresh.Transfer?.Amount == 70 && fresh.PendingReviewButton is not null, "Explicit Arabic amount edit rebuilds summary");
var mobileOnly = new ConversationState { Name = "محمد السالم" };
reply = await Reply(mobileOnly, "٠٥١٢٣٤٥٦٧٨");
Check(mobileOnly.Transfer?.RequestedField == "amount" && reply.Contains("كم") && !reply.Contains("اسم"), "Mobile recipient skips optional recipient name");
reply = await Reply(mobileOnly, "عشرين ريال");
Check(mobileOnly.Transfer?.Amount == 20 && mobileOnly.PendingReviewButton is not null, "Arabic written amount completes mobile-only summary");
reply = await Reply(mobileOnly, "عدل المبلغ إلى واحد وعشرين ريال");
Check(mobileOnly.Transfer?.Amount == 21, "Compound Arabic written amount edits summary");
reply = await Reply(mobileOnly, "عدل المبلغ إلى عشرين ونص ريال");
Check(mobileOnly.Transfer?.Amount == 20.5m, "Written half amount understood");
reply = await Reply(mobileOnly, "عدل المبلغ إلى نص ريال");
Check(reply.Contains("1 ريال") && mobileOnly.Transfer?.Amount == 20.5m && mobileOnly.PendingReviewButton is null, "Below-one written amount rejected without changing existing amount");
reply = await Reply(mobileOnly, "عدل المبلغ إلى ٠٫٥ ريال");
Check(reply.Contains("1 ريال") && mobileOnly.PendingReviewButton is null, "Below-one Arabic numeric amount rejected");
reply = await Reply(mobileOnly, "عدل المبلغ إلى ريال واحد");
Check(mobileOnly.Transfer?.Amount == 1, "Minimum one riyal written amount accepted");
var writtenEnglish = new ConversationState { Name = "John Smith" };
reply = await Reply(writtenEnglish, "send 0512345678 twenty-one SAR");
Check(writtenEnglish.Transfer?.Amount == 21 && writtenEnglish.PendingReviewButton is not null, "English written amount and phone in one message");
reply = await Reply(writtenEnglish, "change amount to one hundred and twenty SAR");
Check(writtenEnglish.Transfer?.Amount == 120, "English compound hundred amount accepted");
var missingPhone = new ConversationState { Name = "محمد السالم" };
reply = await Reply(missingPhone, "٠٥١٢٣٤٥٦٧");
Check(reply.Contains("ناقص 1") && missingPhone.Transfer is null, "Nine-digit phone reports missing digit");
reply = await Reply(missingPhone, "051234567890");
Check(reply.Contains("10") && missingPhone.Transfer is null, "Too-long phone rejected");
reply = await Reply(missingPhone, "0512345678");
await Reply(missingPhone, "واحد");
Check(missingPhone.Transfer?.Amount == 1 && missingPhone.PendingReviewButton is not null, "Bare written amount accepted when requested");
var statsDir = Path.Combine(Path.GetTempPath(), "repay-stats-" + Guid.NewGuid());
var statsOptions = Options.Create(new RePayOptions { DataDirectory = statsDir });
var statsStore = new ConversationStore(statsOptions, new TestEnvironment { ContentRootPath = statsDir });
await statsStore.InitializeAsync();
await using (var session = await statsStore.OpenSessionAsync("966500000010", default))
{
 var state = session.State; state.Name = "محمد السالم"; state.ConversationCount = 3;
 await Reply(state, "send 0500000000 20 SAR");
 await Reply(state, "continue");
 Check(state.RecordedTransferAmounts == 1 && state.TransferAmountTotal == 20, "Repeated summary does not duplicate amount analytics");
 await Reply(state, "change amount to 30 SAR");
 Check(state.RecordedTransferAmounts == 1 && state.TransferAmountTotal == 30, "Editing amount replaces previous contribution");
 await Reply(state, "send 0500000000 50 SAR");
 Check(state.RecordedTransferAmounts == 2 && state.TransferAmountTotal == 80, "Fresh send adds distinct amount contribution");
 await Reply(state, "cancel");
 Check(state.TransferAmountTotal == 80, "Prepared amount history survives cancellation of active draft");
 await session.SaveAsync(default);
}
await using (var session = await statsStore.OpenSessionAsync("966500000011", default))
{
 session.State.Name = "علي السالم"; session.State.ConversationCount = 1; session.State.TransferExamples = 4;
 session.State.Transfer = new TransferDraft { RequestedField = "review", Amount = 40, RecipientPhone = "966500000000" };
 await session.SaveAsync(default);
}
using (var stats = JsonDocument.Parse(JsonSerializer.Serialize(await statsStore.AnalyticsAsync(default))))
{
 var root = stats.RootElement;
 Check(root.GetProperty("totalAmounts").GetDecimal() == 120 && root.GetProperty("averageAmounts").GetDecimal() == 40, "Analytics averages only recorded amounts, preserves available legacy summary");
 Check(root.GetProperty("averageOperations").GetDecimal() == 1.5m && root.GetProperty("conversations").GetInt64() == 4, "Average operations counts completed summaries per user");
 Check(root.GetProperty("transfers").GetInt64() == 3 && root.GetProperty("recordedAmounts").GetInt64() == 3, "Operations count completed recorded summaries only");
 var row = root.GetProperty("userList")[0];
 Check(row.TryGetProperty("operations", out _) && row.TryGetProperty("averageOperations", out _) && row.TryGetProperty("conversations", out _) && !row.GetProperty("mobile").GetString()!.Contains("500000010"), "Dashboard rows include requested counters and mask mobile");
}
await using (var session = await new ConversationStore(statsOptions, new TestEnvironment { ContentRootPath = statsDir }).OpenSessionAsync("966500000010", default))
 Check(session.State.TransferAmountTotal == 80 && session.State.RecordedTransferAmounts == 2, "Aggregate amount history survives restart without active draft");
var arabicIdentity = new ConversationState { Language = "ar", Name = "محمد السيد" };
await Reply(arabicIdentity, "حول عشرين ريال");
reply = await Reply(arabicIdentity, "Mohammed AlSayyed");
Check(arabicIdentity.Language == "ar" && reply.Contains("رقم جوال"), "English recipient name preserves Arabic conversation");
reply = await Reply(arabicIdentity, "0541009449");
Check(reply.Contains("ملخص العملية") && reply.EndsWith("راجع البيانات أو اطلب تعديلها") && !reply.Contains("Operation summary"), "Exact Arabic summary copy and footer");
await Reply(arabicIdentity, "عدل المبلغ");
reply = await Reply(arabicIdentity, "30 SAR");
Check(arabicIdentity.Language == "ar" && reply.Contains("ملخص العملية"), "Currency abbreviation cannot switch numeric reply language");
reply = await Reply(arabicIdentity, "هل الشركة مرخصة؟");
Check(reply.Contains("https://repay.sa") && arabicIdentity.Transfer?.Amount == 30, "Company question routes to website without mutating send");
using(var cta = JsonDocument.Parse(handler.Bodies.First(body=>body.Contains("cta_url"))))
 Check(cta.RootElement.GetProperty("interactive").GetProperty("action").GetProperty("parameters").GetProperty("url").GetString()=="https://repay.sa", "CTA opens exact supplied website without embedding private details");
// Security regressions: identifiers cannot authenticate, blank tokens fail closed.
var verifyRequest = new Microsoft.AspNetCore.Http.DefaultHttpContext().Request;
verifyRequest.QueryString = new Microsoft.AspNetCore.Http.QueryString("?hub.mode=subscribe&hub.verify_token=&hub.challenge=test");
Check(WebhookEndpoints.VerifyAsync(verifyRequest, Options.Create(new RePayOptions())) is Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult { StatusCode: 403 }, "Blank webhook verify token is rejected");
verifyRequest.QueryString = new Microsoft.AspNetCore.Http.QueryString("?hub.mode=subscribe&hub.verify_token=unit-secret&hub.challenge=test");
Check(WebhookEndpoints.VerifyAsync(verifyRequest, Options.Create(new RePayOptions { WhatsAppVerifyToken = "unit-secret" })) is Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult, "Matching configured verify token accepts handshake");
var signatureMethod = typeof(WebhookEndpoints).GetMethod("IsValidSignature", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var signatureBody = "{\"text\":\"مرحبا\"}";
var signature = "sha256=" + Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes("unit-secret"), System.Text.Encoding.UTF8.GetBytes(signatureBody)));
bool Signed(string? header, string body) => (bool)signatureMethod.Invoke(null, new object?[] { header, body, "unit-secret" })!;
Check(Signed(signature, signatureBody) && !Signed(signature, signatureBody + " ") && !Signed(signature[7..], signatureBody) && !Signed(null, signatureBody), "Webhook HMAC rejects tampering, missing prefix and missing signature");
var authContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
authContext.Request.Headers.Authorization = "Bearer 0500000000";
Check(await AnalyticsEndpoints.GetAsync(authContext, Options.Create(new RePayOptions { AnalyticsPhone = "0500000000" }), store) is Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult, "Phone alone cannot authenticate to dashboard");
authContext.Request.Headers.Authorization = "Bearer unit-dashboard-secret";
Check(await AnalyticsEndpoints.GetAsync(authContext, Options.Create(new RePayOptions { AnalyticsPassword = "unit-dashboard-secret" }), store) is not Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult, "Configured dashboard password permits access");
Console.WriteLine("All conversation and analytics checks passed");

sealed class TestProvider : IAgentProvider
{
 public string Action = "clarify"; public string Name => "Test"; public bool IsConfigured => true;
 public Task<string> InterpretAsync(string text, CancellationToken ct) => Task.FromResult(JsonSerializer.Serialize(new { action = Action, value = (string?)null }));
 public Task<string> ReplyAsync(string text, CancellationToken ct) => Task.FromResult("Question answered");
}
sealed class RecordingHandler : HttpMessageHandler
{
 public List<string> Bodies = []; public bool FailNext;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
 {
   var declaredLength = request.Content!.Headers.ContentLength;
   var body = await request.Content.ReadAsStringAsync(ct);
   if (declaredLength is null || declaredLength != System.Text.Encoding.UTF8.GetByteCount(body))
       throw new Exception("Meta payload must have a known UTF-8 Content-Length before sending");
   Bodies.Add(body); if (FailNext) { FailNext = false; return new(HttpStatusCode.ServiceUnavailable); } return new(HttpStatusCode.OK) { Content = new StringContent("{}") }; }
}
sealed class TestEnvironment : IHostEnvironment
{
 public string EnvironmentName { get; set; } = "Testing"; public string ApplicationName { get; set; } = "Tests";
 public string ContentRootPath { get; set; } = ""; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
