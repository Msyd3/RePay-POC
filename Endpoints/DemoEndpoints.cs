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

        await whatsapp.SendTextAsync(
            review.Sender.Phone,
            "هذه عملية تجريبية 👋🏼\n\nكذا بيكون التحويل في ري باي: أرسل رسالة باسم المستفيد أو شارك جهة اتصاله وحدد المبلغ، ثم راجع العملية وأكّدها.\n\nما تم تحويل أي مبلغ.",
            ct);

        return Results.NoContent();
    }
}
