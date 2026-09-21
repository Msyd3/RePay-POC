namespace RePay.WhatsAppPoc.Services;

public interface IAgentProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<string> ReplyAsync(string userText, CancellationToken ct);
}
