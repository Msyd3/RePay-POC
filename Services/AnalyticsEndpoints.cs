using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
namespace RePay.WhatsAppPoc.Services;
public static class AnalyticsEndpoints
{
    public static async Task<IResult> GetAsync(HttpContext context, IOptions<RePayOptions> options, ConversationStore store)
    {
        context.Response.Headers.CacheControl = "no-store";
        var expected = options.Value.AnalyticsPassword;
        var auth = context.Request.Headers.Authorization.ToString();
        if (expected.Length < 24 || !auth.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(auth[7..])), SHA256.HashData(Encoding.UTF8.GetBytes(expected))))
            return Results.Unauthorized();
        return Results.Ok(await store.AnalyticsAsync(context.RequestAborted));
    }
}
