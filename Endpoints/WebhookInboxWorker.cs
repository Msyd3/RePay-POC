namespace RePay.WhatsAppPoc.Services;

public sealed class WebhookInboxWorker(
    ConversationStore store,
    WebhookInboxQueue inbox,
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookInboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WebhookInboxItem? inMemoryRetry = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            WebhookInboxItem? item;
            try
            {
                item = store.HasDurableWebhookInbox
                    ? await store.ClaimWebhookAsync(stoppingToken)
                    : inMemoryRetry is not null
                        ? inMemoryRetry
                        : await inbox.DequeueAsync(stoppingToken);
                inMemoryRetry = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unable to read the WhatsApp webhook inbox");
                await DelayAsync(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            if (item is null)
            {
                await DelayAsync(TimeSpan.FromMilliseconds(500), stoppingToken);
                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<WhatsAppOrchestrator>();
                await orchestrator.HandleAsync(item.Payload, stoppingToken);
                await store.CompleteWebhookAsync(item.Id, stoppingToken);
                logger.LogInformation("WhatsApp webhook processed: EventId={EventId}", item.Id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Leave the leased row in place. It becomes claimable after the
                // lease expires when the service starts again.
                break;
            }
            catch (Exception ex)
            {
                var failedItem = store.HasDurableWebhookInbox
                    ? item
                    : item with { Attempts = item.Attempts + 1 };
                var deadLettered = failedItem.Attempts >= 8;
                if (store.HasDurableWebhookInbox)
                {
                    try
                    {
                        await store.RetryWebhookAsync(failedItem, ex.GetType().Name, stoppingToken);
                    }
                    catch (Exception retryError)
                    {
                        logger.LogError(retryError,
                            "Could not reschedule WhatsApp webhook: EventId={EventId}, Attempt={Attempt}",
                            item.Id, failedItem.Attempts);
                        continue;
                    }
                }
                else if (!deadLettered)
                {
                    var delaySeconds = Math.Min(600, 10 * Math.Pow(2, Math.Max(0, failedItem.Attempts - 1)));
                    inMemoryRetry = failedItem;
                    await DelayAsync(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
                }

                if (deadLettered)
                    logger.LogError(ex,
                        "WhatsApp webhook moved to failed state after retries: EventId={EventId}", item.Id);
                else
                    logger.LogWarning(ex,
                        "WhatsApp webhook processing failed and was scheduled for retry: EventId={EventId}, Attempt={Attempt}",
                        item.Id, failedItem.Attempts);
            }
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
