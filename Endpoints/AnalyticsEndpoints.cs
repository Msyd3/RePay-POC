using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
namespace RePay.WhatsAppPoc.Services;
public static class AnalyticsEndpoints
{
    public static async Task<IResult> GetAsync(HttpContext context, IOptions<RePayOptions> options, ConversationStore store)
    {
        context.Response.Headers.CacheControl = "no-store";
        // A phone number is an identifier, not an authentication secret.
        var expected = options.Value.AnalyticsPassword;
        var auth = context.Request.Headers.Authorization.ToString();
        var supplied = auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..] : "";
        if (string.IsNullOrWhiteSpace(expected) || !auth.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return Results.Unauthorized();
        return Results.Ok(await store.AnalyticsAsync(context.RequestAborted));
    }
}

