namespace RePay.WhatsAppPoc.Services;

public sealed class AgentRouter(IEnumerable<IAgentProvider> providers, ILogger<AgentRouter> logger)
{
    public async Task<IntentDecision> InterpretAsync(string context, CancellationToken ct)
    {
        foreach (var provider in providers.Where(p => p.IsConfigured))
        {
            try
            {
                var decision = ConversationIntent.Parse(await provider.InterpretAsync(context, ct));
                if (decision is not null) return decision;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "Intent provider {Provider} failed; trying fallback", provider.Name); }
        }
        return new("clarify", null);
    }

    public async Task<string> ReplyAsync(string userText, CancellationToken ct)
    {
        var configured = providers.Where(provider => provider.IsConfigured).ToList();
        if (configured.Count == 0)
            throw new InvalidOperationException("No agent provider is configured.");

        Exception? lastError = null;
        foreach (var provider in configured)
        {
            try { return await provider.ReplyAsync(userText, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastError = ex;
                logger.LogWarning(ex, "Agent provider {Provider} failed; trying the next provider.", provider.Name);
            }
        }

        throw new InvalidOperationException("All configured agent providers failed.", lastError);
    }
}
