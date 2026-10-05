using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Npgsql;

namespace RePay.WhatsAppPoc.Services;

public sealed record ChatTurn(string Role, string Text);
public sealed class ConversationState
{
    public string? Name { get; set; }
    public bool AwaitingNameChange { get; set; }
    public DemoState? Demo { get; set; }
    public long MessageCount { get; set; }
    public long ConversationCount { get; set; }
    public long SearchExamples { get; set; }
    public long TransferExamples { get; set; }
    public long PaymentExamples { get; set; }
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? PendingMessageId { get; set; }
    public string? PendingReply { get; set; }
    public string? PendingFollowUp { get; set; }
    public int PendingReplyIndex { get; set; }
    public bool Welcomed { get; set; }
    public List<ChatTurn> Turns { get; set; } = [];
    public List<string> ProcessedMessages { get; set; } = [];
}

public sealed class ConversationStore(IOptions<RePayOptions> options, IHostEnvironment environment)
{
    // Fixed stripes bound memory and serialize a user's requests on this single-instance POC.
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private string DirectoryPath => Path.GetFullPath(options.Value.DataDirectory, environment.ContentRootPath);
    private DbConnection CreateConnection()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.DatabaseConnectionString))
            return new NpgsqlConnection(options.Value.DatabaseConnectionString);
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

    public async Task<object> AnalyticsAsync(CancellationToken ct)
    {
        await using var db = CreateConnection();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT phone,state FROM repay_conversations";
        await using var rows = await cmd.ExecuteReaderAsync(ct);
        var userList = new List<object>();
        long users = 0, conversations = 0, messages = 0, searches = 0, transfers = 0, payments = 0;
        while (await rows.ReadAsync(ct))
        {
            var s = JsonSerializer.Deserialize<ConversationState>(rows.GetString(1))!;
            var phone = rows.GetString(0);
            userList.Add(new { name = s.Name ?? "لم يحدد الاسم", lastFour = phone.Length >= 4 ? phone[^4..] : "", messages = s.MessageCount });
            users++;
            conversations += s.ConversationCount;
            messages += s.MessageCount;
            searches += s.SearchExamples;
            transfers += s.TransferExamples;
            payments += s.PaymentExamples;
        }
        return new { userList, users, conversations, messages, searches, transfers, payments, actualTransactions = 0, updatedAt = DateTimeOffset.UtcNow };
    }

    private static void Add(DbCommand cmd, string name, string value)
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
