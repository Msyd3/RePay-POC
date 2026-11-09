using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Npgsql;

namespace RePay.WhatsAppPoc.Services;

public sealed record ChatTurn(string Role, string Text);
public sealed record SharedContact(string Name, string Phone);
public sealed record WebhookInboxItem(string Id, string Payload, int Attempts);
public sealed class ConversationState
{
    public string? Name { get; set; }
    public List<SharedContact> Contacts { get; set; } = [];
    public string Language { get; set; } = "ar";
    public TransferDraft? Transfer { get; set; }
    public string? PendingReviewButton { get; set; }
    public bool AwaitingNameChange { get; set; }
    public string? Focus { get; set; }
    public bool AwaitingClarification { get; set; }
    public DemoState? Demo { get; set; }
    public long MessageCount { get; set; }
    public long ConversationCount { get; set; }
    public long SearchExamples { get; set; }
    public long TransferExamples { get; set; }
    public decimal TransferAmountTotal { get; set; }
    public long RecordedTransferAmounts { get; set; }
    public long PaymentExamples { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? PendingMessageId { get; set; }
    public string? PendingReply { get; set; }
    public string? PendingFollowUp { get; set; }
    public int PendingReplyIndex { get; set; }
    public bool Welcomed { get; set; }
    public List<ChatTurn> Turns { get; set; } = [];
    public List<string> ProcessedMessages { get; set; } = [];
    public void RecordTransferAmount()
    {
        if (Transfer is not { RequestedField: "review", Amount: { } amount } draft || !SendAmount.Valid(amount)) return;
        if (draft.RecordedAmount is { } previous) TransferAmountTotal += amount - previous;
        else { TransferAmountTotal += amount; RecordedTransferAmounts++; }
        draft.RecordedAmount = amount;
    }

}

public sealed class ConversationStore(IOptions<RePayOptions> options, IHostEnvironment environment)
{
    // Fixed stripes bound memory and serialize a user's requests on this single-instance POC.
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private readonly SemaphoreSlim _inboxLock = new(1, 1);
    public bool HasDurableWebhookInbox => !string.IsNullOrWhiteSpace(options.Value.DatabaseConnectionString);
    private string DirectoryPath => Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
    private DbConnection CreateConnection()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.DatabaseConnectionString))
            return new NpgsqlConnection(options.Value.DatabaseConnectionString);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("K_SERVICE")))
            throw new InvalidOperationException("Cloud Run requires durable PostgreSQL configuration; SQLite is ephemeral.");
        Directory.CreateDirectory(DirectoryPath);
        return new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(DirectoryPath, "repay.db") }.ToString());
    }

    public async Task InitializeAsync()
    {
        await using var db = CreateConnection();
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS repay_conversations (phone TEXT PRIMARY KEY, state TEXT NOT NULL)";
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS repay_webhook_inbox (
                id TEXT PRIMARY KEY,
                payload TEXT,
                status TEXT NOT NULL,
                attempts INTEGER NOT NULL,
                available_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                lease_until TEXT,
                last_error TEXT
            )
            """;
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS repay_webhook_inbox_due ON repay_webhook_inbox(status,available_at,created_at)";
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS repay_transfer_reviews (id TEXT PRIMARY KEY, payload TEXT NOT NULL, expires_at TEXT NOT NULL)";
        await cmd.ExecuteNonQueryAsync();
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS repay_transfer_reviews_expiry ON repay_transfer_reviews(expires_at)";
        await cmd.ExecuteNonQueryAsync();
        // Preserve legacy names on upgrades when the original file is still available.
        var legacy = Path.Combine(environment.ContentRootPath, "App_Data", "users.json");
        if (!File.Exists(legacy)) return;
        using var users = JsonDocument.Parse(await File.ReadAllTextAsync(legacy));
        foreach (var user in users.RootElement.EnumerateArray())
        {
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO repay_conversations(phone,state) VALUES (@phone,@state) ON CONFLICT(phone) DO NOTHING";
            Add(cmd, "phone", user.GetProperty("Phone").GetString()!);
            Add(cmd, "state", JsonSerializer.Serialize(new ConversationState { Name = user.GetProperty("Name").GetString(), Welcomed = true }));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<string> EnqueueWebhookAsync(string payload, CancellationToken ct)
    {
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO repay_webhook_inbox(id,payload,status,attempts,available_at,created_at,lease_until,last_error)
            VALUES (@id,@payload,'pending',0,@now,@now,NULL,NULL)
            ON CONFLICT(id) DO NOTHING
            """;
        Add(cmd, "id", id);
        Add(cmd, "payload", payload);
        Add(cmd, "now", now);
        await cmd.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task<WebhookInboxItem?> ClaimWebhookAsync(CancellationToken ct)
    {
        await _inboxLock.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var nowText = now.ToString("O");
            var leaseText = now.AddMinutes(2).ToString("O");
            await using var db = CreateConnection();
            await db.OpenAsync(ct);
            await using var select = db.CreateCommand();
            select.CommandText = """
                SELECT id,payload,attempts FROM repay_webhook_inbox
                WHERE payload IS NOT NULL AND (
                    (status='pending' AND available_at <= @now)
                    OR (status='processing' AND lease_until <= @now)
                )
                ORDER BY created_at LIMIT 1
                """;
            Add(select, "now", nowText);
            await using var reader = await select.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            var id = reader.GetString(0);
            var payload = reader.GetString(1);
            var attempts = reader.GetInt32(2) + 1;
            await reader.DisposeAsync();

            await using var update = db.CreateCommand();
            update.CommandText = "UPDATE repay_webhook_inbox SET status='processing',attempts=@attempts,lease_until=@lease WHERE id=@id";
            Add(update, "attempts", attempts);
            Add(update, "lease", leaseText);
            Add(update, "id", id);
            await update.ExecuteNonQueryAsync(ct);
            return new WebhookInboxItem(id, payload, attempts);
        }
        finally { _inboxLock.Release(); }
    }

    public async Task CompleteWebhookAsync(string id, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM repay_webhook_inbox WHERE id=@id";
        Add(cmd, "id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RetryWebhookAsync(WebhookInboxItem item, string errorType, CancellationToken ct)
    {
        const int maxAttempts = 8;
        var isDeadLetter = item.Attempts >= maxAttempts;
        var delaySeconds = Math.Min(600, 10 * Math.Pow(2, Math.Max(0, item.Attempts - 1)));
        var availableAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds).ToString("O");
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = isDeadLetter
            ? "UPDATE repay_webhook_inbox SET status='failed',payload=NULL,lease_until=NULL,last_error=@error WHERE id=@id"
            : "UPDATE repay_webhook_inbox SET status='pending',available_at=@available,lease_until=NULL,last_error=@error WHERE id=@id";
        Add(cmd, "id", item.Id);
        Add(cmd, "error", errorType);
        if (!isDeadLetter) Add(cmd, "available", availableAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Session> OpenSessionAsync(string phone, CancellationToken ct)
    {
        var gate = _locks[(uint)StringComparer.Ordinal.GetHashCode(phone) % (uint)_locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            await using var db = CreateConnection();
            await db.OpenAsync(ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT state FROM repay_conversations WHERE phone=@phone";
            Add(cmd, "phone", phone);
            var raw = await cmd.ExecuteScalarAsync(ct) as string;
            var state = raw is null ? new ConversationState() : JsonSerializer.Deserialize<ConversationState>(raw) ?? throw new InvalidDataException("Invalid conversation state");
            state.RecordTransferAmount(); // Preserve an available legacy summary before idle expiry/new requests.
            return new Session(this, phone, state, gate);
        }
        catch { gate.Release(); throw; }
    }

    private async Task SaveAsync(string phone, ConversationState state, CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO repay_conversations(phone,state) VALUES (@phone,@state) ON CONFLICT(phone) DO UPDATE SET state=excluded.state";
        Add(cmd, "phone", phone);
        Add(cmd, "state", JsonSerializer.Serialize(state));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task PublishTransferReviewAsync(TransferReview review, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var expiresText = expiresAt.ToUniversalTime().ToString("O");
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM repay_transfer_reviews WHERE expires_at <= @now";
        Add(cmd, "now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        cmd.Parameters.Clear();
        cmd.CommandText = "INSERT INTO repay_transfer_reviews(id,payload,expires_at) VALUES (@id,@payload,@expires) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload,expires_at=excluded.expires_at";
        Add(cmd, "id", review.Id);
        Add(cmd, "payload", JsonSerializer.Serialize(review));
        Add(cmd, "expires", expiresText);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TransferReview?> GetTransferReviewAsync(string id, CancellationToken ct)
    {
        if (id.Length != 32 || !id.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f')) return null;
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM repay_transfer_reviews WHERE id=@id AND expires_at > @now";
        Add(cmd, "id", id);
        Add(cmd, "now", DateTimeOffset.UtcNow.ToString("O"));
        var payload = await cmd.ExecuteScalarAsync(ct) as string;
        return payload is null ? null : JsonSerializer.Deserialize<TransferReview>(payload);
    }

    public async Task<object> AnalyticsAsync(CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT phone,state FROM repay_conversations";
        await using var rows = await cmd.ExecuteReaderAsync(ct);
        var userList = new List<object>();
        long users = 0, conversations = 0, transfers = 0, repeats = 0, recordedAmounts = 0;
        decimal totalAmounts = 0;
        while (await rows.ReadAsync(ct))
        {
            var s = JsonSerializer.Deserialize<ConversationState>(rows.GetString(1))!;
            s.RecordTransferAmount();
            var phone = rows.GetString(0);
            var repeat = s.RecordedTransferAmounts;
            userList.Add(new { name = s.Name ?? "لم يحدد الاسم", mobile = ConversationLifecycle.MaskPhone(phone), operations = s.RecordedTransferAmounts, averageOperations = s.ConversationCount == 0 ? 0 : (decimal)s.RecordedTransferAmounts / s.ConversationCount, conversations = s.ConversationCount });
            users++;
            conversations += s.ConversationCount;
            transfers += s.RecordedTransferAmounts;
            repeats += repeat;
            recordedAmounts += s.RecordedTransferAmounts;
            totalAmounts += s.TransferAmountTotal;
        }
        return new { userList, users, conversations, transfers, totalAmounts,
            averageAmounts = recordedAmounts == 0 ? 0 : totalAmounts / recordedAmounts,
            averageOperations = users == 0 ? 0 : (decimal)repeats / users,
            recordedAmounts, updatedAt = DateTimeOffset.UtcNow };
    }

    private static void Add(DbCommand cmd, string name, string value)
    {
        var parameter = cmd.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; cmd.Parameters.Add(parameter);
    }

    private static void Add(DbCommand cmd, string name, int value)
    {
        var parameter = cmd.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; cmd.Parameters.Add(parameter);
    }

    public sealed class Session(ConversationStore store, string phone, ConversationState state, SemaphoreSlim gate) : IAsyncDisposable
    {
        public ConversationState State { get; } = state;
        public Task SaveAsync(CancellationToken ct) => store.SaveAsync(phone, State, ct);
        public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; }
    }
}
