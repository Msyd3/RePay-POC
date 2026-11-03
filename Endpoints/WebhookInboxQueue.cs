using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace RePay.WhatsAppPoc.Services;

public sealed class WebhookInboxQueue
{
    private readonly Channel<WebhookInboxItem> _channel = Channel.CreateBounded<WebhookInboxItem>(
        new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(string payload)
    {
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        return _channel.Writer.TryWrite(new WebhookInboxItem(id, payload, 0));
    }

    public ValueTask<WebhookInboxItem> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
