# RePay WhatsApp POC

An ASP.NET Core (.NET 10) WhatsApp assistant for collecting send details in Arabic and English. **It does not execute financial transactions.** The confirmation button currently opens https://repay.sa without personal details in the URL.

## Start here

- [Architecture and file map](docs/architecture.md)
- [Configuration and operations](docs/operations.md)
- [Google Cloud Run migration](docs/cloud-run.md)
- [Security boundaries](SECURITY.md)
- [Data handling](PRIVACY.md)

## Local development

Install .NET 10 SDK. Configure secrets through environment variables or .NET user secrets; `.env.example` is a reference and is not loaded automatically. Do not put real secrets in appsettings.json.

```sh
dotnet restore
dotnet run --project RePay.WhatsAppPoc.csproj
```

SQLite is a local development fallback. Use durable PostgreSQL for hosted deployments.

```sh
dotnet run --project tests/ConversationChecks.csproj
node tests/dashboard-checks.cjs
```

The checks use fake AI/HTTP providers and temporary SQLite databases, not live messages or production credentials.

## Behavior

Arabic messages receive Arabic replies, English messages receive English replies; numeric replies and shared contacts preserve language. Send requests accept a name, mobile or shared contact and numeric/written amounts. Sender first/last name is required; recipient name is optional with a valid Saudi mobile. Minimum amount is SAR 1. The assistant asks only missing details and supports edits and cancellations. New sends cannot reuse the previous amount.

Contacts resolve only from cards explicitly shared in this chat, not the phone address book. A completed summary is counted as a POC operation, **not an executed transfer**. Edits update the same summary amount without counting another operation. Dashboard operations are completed summaries; average operations per user is total summaries/users, and row average is summaries/conversations.

Company questions link to the official website. WhatsApp determines whether the URL button opens an internal or external browser; the API cannot force it. Native Flows, Apple Pay, recipient SMS, OTP, bank linking and withdrawals are not implemented.

## Deployment incident, 14 October 2026

Observed Render deploy requests returned `Service Unavailable`; `/health` stayed on Render's loading screen. Render's status page reported a builds/deploys incident. A restart was recorded but did not establish successful application startup. This is evidence of a hosting blocker, not proof that the code is fault-free.

The business sender has been registered and its webhook subscription configured in Meta. These are runtime account settings, not credentials or data to commit here. Verify actual delivery and automatic replies after the new host is running.
