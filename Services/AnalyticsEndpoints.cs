using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
namespace RePay.WhatsAppPoc.Services;
public static class AnalyticsEndpoints
{
    public static async Task<IResult> GetAsync(HttpContext context, IOptions<RePayOptions> options, ConversationStore store)
    {
        context.Response.Headers.CacheControl = "no-store";
        var phoneMode = !string.IsNullOrWhiteSpace(options.Value.AnalyticsPhone);
        var expected = phoneMode ? ConversationLifecycle.EnglishDigits(options.Value.AnalyticsPhone).Trim() : options.Value.AnalyticsPassword;
        var auth = context.Request.Headers.Authorization.ToString();
        var supplied = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..] : "";
        if (phoneMode) supplied = ConversationLifecycle.EnglishDigits(supplied).Trim();
        if (string.IsNullOrWhiteSpace(expected) || !auth.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return Results.Unauthorized();
        return Results.Ok(await store.AnalyticsAsync(context.RequestAborted));
    }
}
