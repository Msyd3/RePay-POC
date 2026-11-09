namespace RePay.WhatsAppPoc.Services;

public static class TransferReviewEndpoints
{
    public static async Task<IResult> GetAsync(string id, ConversationStore store, HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        var review = await store.GetTransferReviewAsync(id, ct);
        if (review is null) return Results.NotFound();
        var masked = review with
        {
            Sender = review.Sender with { Name = MaskName(review.Sender.Name), Phone = ConversationLifecycle.MaskPhone(review.Sender.Phone) },
            Recipient = review.Recipient with { Name = MaskName(review.Recipient.Name), Phone = ConversationLifecycle.MaskPhone(review.Recipient.Phone) }
        };
        return Results.Ok(masked);
    }

    private static string MaskName(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Length <= 1 ? part + "***" : part[..1] + new string('*', Math.Min(part.Length - 1, 8))));
}
