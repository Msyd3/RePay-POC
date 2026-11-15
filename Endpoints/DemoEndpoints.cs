namespace RePay.WhatsAppPoc.Services;

public static class DemoEndpoints
{
    public static async Task<IResult> NotifyTransferReviewOpenedAsync(
        string id,
        ConversationStore store,
        MetaWhatsAppClient whatsapp,
        HttpContext context,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        var review = await store.GetTransferReviewAsync(id, ct);
        if (review is null) return Results.NotFound();

        if (!await store.TryMarkDemoNotificationAsync(id, ct))
            return Results.NoContent();

        try
        {
            var waitlistNumber = await store.EnsureWaitlistNumberAsync(review.Sender.Phone, ct);
            await whatsapp.SendTextAsync(
                review.Sender.Phone,
                $"هذه عملية تجريبية 👋🏼\nرقمك في قائمة الانتظار {waitlistNumber}",
                ct);
        }
        catch
        {
            await store.RemoveDemoNotificationMarkAsync(id, ct);
            throw;
        }

        return Results.NoContent();
    }
}
