# Architecture

## File ownership

| Directory | Responsibility |
| --- | --- |
| `Program.cs` | Dependency registration, HTTP pipeline, routes and startup |
| `Configuration/` | Configuration contract (`RePayOptions`) |
| `Endpoints/` | Meta webhook validation and authenticated aggregate API |
| `Application/` | WhatsApp orchestration, checkpoints, retries and sequencing |
| `Domain/Conversations/` | Language, transfer slots, edits, names, amount parsing and lifecycle |
| `Infrastructure/WhatsApp/` | Meta Graph API HTTP payloads |
| `Infrastructure/AI/` | Groq/Gemini clients, provider selection and fallback |
| `Infrastructure/Persistence/` | PostgreSQL/SQLite conversations, aggregate metrics, local locks |
| `Legacy/` | Compatibility helpers and old providers; not registered as active providers |
| `wwwroot/` | Dashboard and bundled font |
| `tests/` | Deterministic conversation, storage, payload and UI regression checks |
| `docs/` | Operational handover and migration guidance |

The existing C# namespace is preserved during this directory refactor to avoid changing callers and stored model types. Some compatibility types remain referenced by persisted state; do not delete Legacy blindly.

## Message path

1. Meta POSTs `/webhooks/whatsapp`.
2. Endpoint verifies `X-Hub-Signature-256` using the app secret; malformed/unsigned requests fail closed.
3. Orchestrator ignores status-only payloads and stale messages, locks per user, loads state and deduplicates IDs.
4. Domain code extracts deterministic slots and asks the model for contextual interpretation where needed. Shared phone numbers are withheld from model context.
5. Pending response is saved before sending. Meta client sends the text or website button; delivery checkpoint is saved after acceptance.
6. Meta may later report failed delivery even after accepting a send request. Acceptance is not delivery; current code does not persist delivery statuses.

## Persistence and concurrency limits

State is JSON keyed by sender phone in `repay_conversations`. Parameterized SQL is used. PostgreSQL preserves data between revisions; container-local SQLite does not.

Locks are process-local and deduplication history is bounded. This POC is not safe for unrestricted horizontal scaling, and sending plus saving a checkpoint is not an atomic transaction with Meta. For production implement distributed per-user serialization, durable inbox/outbox, message status persistence, retention/deletion and authenticated administrative identities. Cloud Run max-instance settings are operational limits, not a distributed-lock guarantee (revision overlap may still happen).
