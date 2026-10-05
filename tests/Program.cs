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
string Payload(string id, string text, string phone = "966500000001") => JsonSerializer.Serialize(new { entry = new[] { new { changes = new[] { new { value = new { messages = new[] { new { id, from = phone, text = new { body = text } } } } } } } } });
void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
await app.HandleAsync(Payload("m1", "مرحبا"), default);
Check(handler.Replies.Last() == ConversationLifecycle.Format(ServiceConversation.Welcome), "Arabic welcome with no footer");
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
Check(demoReply.Contains("الشركات السعودية") && demoReply.Contains("ما صار بحث"), "Saudi-default purchase example never claims live search");
Check(demoState.TransferExamples == 1 && demoState.SearchExamples == 1 && demoState.PaymentExamples == 1, "Example counters have explicit meanings");
await app.HandleAsync(Payload("demo1", "جرب تحويل"), default);
var contactPayload = JsonSerializer.Serialize(new { entry = new[] { new { changes = new[] { new { value = new { messages = new[] { new { id = "demo2", from = "966500000001", contacts = new[] { new { name = new { formatted_name = "خالد" }, phones = new[] { new { phone = "0500000000" } } } } } } } } } } } });
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
await app.HandleAsync(Payload("name-during-demo", "ممكن تغير الاسم؟"), default);
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

Console.WriteLine("All conversation checks passed.");

sealed class TestProvider : IAgentProvider
{
 public string Name => "Test"; public bool IsConfigured => true; public int Calls; public string LastInput = "";
 public Task<string> ReplyAsync(string text, CancellationToken ct) { Calls++; LastInput = text; return Task.FromResult("الخدمات بتتاح تدريجيًا، وحاليًا أجاوب عن أسئلتك عنها."); }
}
sealed class RecordingHandler : HttpMessageHandler
{
 public List<string> Replies = []; public string? LastGroqBody; public bool FailNext; public int FailAfter = -1;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
 {
  var body = await request.Content!.ReadAsStringAsync(ct);
  if (request.RequestUri!.Host == "api.groq.com") { LastGroqBody = body; return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"الخدمات قريبًا\"}}]}") }; }
  if (request.RequestUri.Host != "graph.facebook.com") throw new Exception("Unexpected browser/network call");
  if (FailAfter == 0) { FailAfter = -1; return new(HttpStatusCode.ServiceUnavailable); }
  if (FailAfter > 0) FailAfter--;
  if (FailNext) { FailNext = false; return new(HttpStatusCode.ServiceUnavailable); }
  using var json = JsonDocument.Parse(body); Replies.Add(json.RootElement.GetProperty("text").GetProperty("body").GetString()!);
  return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
 }
}
sealed class TestEnvironment : IHostEnvironment
{
 public string EnvironmentName { get; set; } = "Testing"; public string ApplicationName { get; set; } = "Tests";
 public string ContentRootPath { get; set; } = ""; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
