namespace RePay.WhatsAppPoc.Services;

public sealed class RePayOptions
{
    public string WhatsAppVerifyToken { get; init; } = "";
    public string MetaAppSecret { get; init; } = "";
    public string MetaAccessToken { get; init; } = "";
    public string MetaPhoneNumberId { get; init; } = "";
    public string MetaGraphApiVersion { get; init; } = "v25.0";
    public string OpenAiApiKey { get; init; } = "";
    public string OpenAiModel { get; init; } = "gpt-5-mini";
    public string BrowserAgentBaseUrl { get; init; } = "";
}
