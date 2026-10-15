using Microsoft.AspNetCore.RateLimiting;
using RePay.WhatsAppPoc.Services;

var builder = WebApplication.CreateBuilder(args);
// Cloud Run supplies PORT; Render can continue using the container default.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port) && port is > 0 and <= 65535)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 1024 * 1024);
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
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler(handler =>
    handler.Run(context => { context.Response.StatusCode = 500; return context.Response.WriteAsync("Request failed."); }));
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});
app.UseRateLimiter();
app.UseStaticFiles();
app.MapGet("/analytics", () => Results.Redirect("/analytics.html"));
app.MapGet("/api/analytics/login-mode", (Microsoft.Extensions.Options.IOptions<RePayOptions> options, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { phone = false });
});
app.MapGet("/api/analytics", AnalyticsEndpoints.GetAsync).RequireRateLimiting("analytics");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/webhooks/whatsapp", WebhookEndpoints.VerifyAsync);
app.MapPost("/webhooks/whatsapp", WebhookEndpoints.ReceiveAsync);
await app.Services.GetRequiredService<ConversationStore>().InitializeAsync();
app.Logger.LogInformation("WhatsApp sender PhoneNumberId: {PhoneNumberId}",
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RePayOptions>>().Value.MetaPhoneNumberId);
app.Run();

