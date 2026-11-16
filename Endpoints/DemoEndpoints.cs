namespace RePay.WhatsAppPoc.Services;

public static class DemoEndpoints
{
    // Opening a review must never send a second WhatsApp message. The follow-up
    // is intentionally sent only after the user taps the existing confirmation button.
    public static IResult NotifyTransferReviewOpenedAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        return Results.NoContent();
    }

    public static async Task<IResult> NotifyTransferReviewConfirmedAsync(
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
            var referralCode = await store.EnsureReferralCodeAsync(review.Sender.Phone, ct);
            var inviteUrl = $"https://wallet.repay.sa/waitlist/?ref={Uri.EscapeDataString(referralCode)}";
            await whatsapp.SendWaitlistCtaAsync(
                review.Sender.Phone,
                $"هذه عملية تجريبية 👋🏼\nترتيبك في قائمة الانتظار: {waitlistNumber}\nرابط دعوتك الخاص: {inviteUrl}",
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
