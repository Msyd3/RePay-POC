namespace RePay.WhatsAppPoc.Services;

public interface IAgentProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<string> ReplyAsync(string userText, CancellationToken ct);
    Task<string> InterpretAsync(string context, CancellationToken ct) => Task.FromResult("{\"action\":\"clarify\",\"value\":null}");
}
