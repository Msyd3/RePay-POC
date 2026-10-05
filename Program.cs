using Microsoft.AspNetCore.RateLimiting;
using RePay.WhatsAppPoc.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<RePayOptions>(builder.Configuration.GetSection("RePay"));
builder.Services.AddHttpClient<MetaWhatsAppClient>();
builder.Services.AddHttpClient<GroqAgentClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient<GeminiAgentClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<IAgentProvider>(sp => sp.GetRequiredService<GroqAgentClient>());
builder.Services.AddScoped<IAgentProvider>(sp => sp.GetRequiredService<GeminiAgentClient>());
builder.Services.AddScoped<AgentRouter>();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.AddScoped<WhatsAppOrchestrator>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("analytics", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
var app = builder.Build();
app.UseRateLimiter();
app.UseStaticFiles();
app.MapGet("/analytics", () => Results.Redirect("/analytics.html"));
app.MapGet("/api/analytics", AnalyticsEndpoints.GetAsync).RequireRateLimiting("analytics");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/webhooks/whatsapp", WebhookEndpoints.VerifyAsync);
app.MapPost("/webhooks/whatsapp", WebhookEndpoints.ReceiveAsync);
await app.Services.GetRequiredService<ConversationStore>().InitializeAsync();
app.Run();
