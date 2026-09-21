namespace RePay.WhatsAppPoc.Services;

public sealed class AgentRouter(IEnumerable<IAgentProvider> providers, ILogger<AgentRouter> logger)
{
    public async Task<string> ReplyAsync(string userText, CancellationToken ct)
    {
        var configured = providers.Where(provider => provider.IsConfigured).ToList();
        if (configured.Count == 0)
            throw new InvalidOperationException("No agent provider is configured.");

        Exception? lastError = null;
        foreach (var provider in configured)
        {
            try { return await provider.ReplyAsync(userText, ct); }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "Agent provider {Provider} failed; trying the next provider.", provider.Name);
            }
        }

        throw new InvalidOperationException("All configured agent providers failed.", lastError);
    }
}
