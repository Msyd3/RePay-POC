using RePay.WhatsAppPoc.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<RePayOptions>(builder.Configuration.GetSection("RePay"));
builder.Services.AddHttpClient<MetaWhatsAppClient>();
builder.Services.AddHttpClient<OpenAiAgentClient>();
builder.Services.AddHttpClient<SafeBrowserAgent>();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.AddScoped<WhatsAppOrchestrator>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/webhooks/whatsapp", WebhookEndpoints.VerifyAsync);
app.MapPost("/webhooks/whatsapp", WebhookEndpoints.ReceiveAsync);
app.Run();
