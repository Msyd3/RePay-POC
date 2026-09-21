namespace RePay.WhatsAppPoc.Services;

public sealed class RePayOptions
{
    public string WhatsAppVerifyToken { get; init; } = "";
    public string MetaAppSecret { get; init; } = "";
    public string MetaAccessToken { get; init; } = "";
    public string MetaPhoneNumberId { get; init; } = "";
    public string MetaGraphApiVersion { get; init; } = "v25.0";
    public string GroqApiKey { get; init; } = "";
    public string GroqModel { get; init; } = "groq/compound-mini";
    public string GeminiApiKey { get; init; } = "";
    public string GeminiModel { get; init; } = "gemini-2.5-flash";
    public string BrowserAgentBaseUrl { get; init; } = "";
}
