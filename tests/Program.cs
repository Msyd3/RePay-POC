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
Check(handler.Replies.Last() == ServiceConversation.Welcome, "Arabic welcome with no footer");
await app.HandleAsync(Payload("m2", "وش الخدمات؟"), default);
Check(provider.Calls == 1, "Questions during onboarding do not become a name");
await app.HandleAsync(Payload("m3", "اسمي محمد"), default);
Check(handler.Replies.Last().Contains("تشرفنا يا محمد"), "Name extracted and saved");
var restarted = new ConversationStore(options, env);
await restarted.InitializeAsync();
app = new WhatsAppOrchestrator(meta, router, restarted);
await app.HandleAsync(Payload("m4", "هل تقدر تحول الآن؟"), default);
Check(JsonDocument.Parse(provider.LastInput).RootElement.GetProperty("name").GetString() == "محمد" && !provider.LastInput.Contains("966500000001"), "Restart preserves name without passing phone to AI");
var count = handler.Replies.Count;
await app.HandleAsync(Payload("m4", "هل تقدر تحول الآن؟"), default);
Check(handler.Replies.Count == count, "Duplicate message ignored across persisted state");
await app.HandleAsync(Payload("m5", "1"), default);
Check(provider.Calls == 3, "Numeric reply is normal conversation, never checkout");
await app.HandleAsync(Payload("m6", "hello", "15550000001"), default);
Check(handler.Replies.Last().Contains("السعودية"), "Saudi number restriction preserved");
await new GroqAgentClient(new HttpClient(handler), options).ReplyAsync("متى الإطلاق؟", default);
using var request = JsonDocument.Parse(handler.LastGroqBody!);
Check(request.RootElement.GetProperty("model").GetString() == ServiceConversation.ChatModel && !request.RootElement.TryGetProperty("tools", out _), "Compound override cannot enable search tools");
Check(!ServiceConversation.TryGetName("123456", out _) && !ServiceConversation.TryGetName("مرحبا", out _), "Numbers and greetings rejected as names");
await Task.WhenAll(app.HandleAsync(Payload("m7", "شكرا"), default), app.HandleAsync(Payload("m7", "شكرا"), default));
Check(handler.Replies.Count == count + 3, "Concurrent duplicate serialized");
Console.WriteLine("All conversation checks passed.");

sealed class TestProvider : IAgentProvider
{
 public string Name => "Test"; public bool IsConfigured => true; public int Calls; public string LastInput = "";
 public Task<string> ReplyAsync(string text, CancellationToken ct) { Calls++; LastInput = text; return Task.FromResult("الخدمات بتتاح تدريجيًا، وحاليًا أجاوب عن أسئلتك عنها."); }
}
sealed class RecordingHandler : HttpMessageHandler
{
 public List<string> Replies = []; public string? LastGroqBody;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
 {
  var body = await request.Content!.ReadAsStringAsync(ct);
  if (request.RequestUri!.Host == "api.groq.com") { LastGroqBody = body; return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"الخدمات قريبًا\"}}]}") }; }
  if (request.RequestUri.Host != "graph.facebook.com") throw new Exception("Unexpected browser/network call");
  using var json = JsonDocument.Parse(body); Replies.Add(json.RootElement.GetProperty("text").GetProperty("body").GetString()!);
  return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
 }
}
sealed class TestEnvironment : IHostEnvironment
{
 public string EnvironmentName { get; set; } = "Testing"; public string ApplicationName { get; set; } = "Tests";
 public string ContentRootPath { get; set; } = ""; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
