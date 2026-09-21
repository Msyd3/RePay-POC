namespace RePay.WhatsAppPoc.Services;

public sealed class RePayOptions
{
    public string WhatsAppVerifyToken { get; init; } = "";
    public string MetaAppSecret { get; init; } = "";
    public string MetaAccessToken { get; init; } = "";
    public string MetaPhoneNumberId { get; init; } = "";
    public string MetaGraphApiVersion { get; init; } = "v25.0";
    // Kept for source compatibility with the original POC client. The active
    // agent router uses Groq first and Gemini as its fallback.
    public string OpenAiApiKey { get; init; } = "";
    public string OpenAiModel { get; init; } = "gpt-4.1-mini";
    public string GroqApiKey { get; init; } = "";
    public string GroqModel { get; init; } = "groq/compound-mini";
    public string GeminiApiKey { get; init; } = "";
    public string GeminiModel { get; init; } = "gemini-2.5-flash";
    public string BrowserAgentBaseUrl { get; init; } = "";
}
