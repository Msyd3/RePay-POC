# RePay POC

A conversational introduction to RePay's upcoming services. RePay plans to offer browsing, transfers, and payments with gradual availability to users. This version only answers service questions: it does not search, browse, prepare checkout, transfer money, or make payments.

## Conversation

New Saudi WhatsApp users receive:

> أهلًا بك في ري باي 👋🏼
>
> أنا مساعدك للتعرّف على خدمات ري باي القادمة في التصفح والتحويل والدفع بأمان، دون مشاركة بياناتك المالية الحساسة مع الوكيل. الخدمات بتتاح تدريجيًا للمستخدمين، وحاليًا أقدر أجاوب عن أسئلتك عنها.
>
> ممكن تشرفني باسمك؟

The sender number comes from the signed WhatsApp webhook; users are never asked to type it. Once their name is saved, subsequent messages go directly to a natural service conversation without numbered menus or a footer. Questions during onboarding can be answered without forcing a name. No launch dates, pricing, licensing claims, or financial transactions are promised.

Groq uses the pinned text-only `qwen/qwen3.8-27b` model; stale Compound settings are ignored deliberately. Gemini is an optional fallback without grounding or tools. Both share one service policy and bounded recent conversation context. Phone numbers are not sent to the model. `SafeBrowserAgent` is legacy code and is not registered or called by this flow.

## Storage

Set `RePay__DatabaseConnectionString` to a PostgreSQL Npgsql connection string in your hosting secrets:

```text
Host=YOUR-HOST;Database=neondb;Username=YOUR-USER;Password=YOUR-PASSWORD;SSL Mode=VerifyFull
```

The app creates `repay_conversations` on startup with a unique phone key. It stores the name, onboarding state, last eight conversation messages, and last 100 processed message IDs. The database must be private; never commit connection strings or customer data. Old `App_Data/users.json` names are imported without overwriting existing database rows when that file is still available.

Without a connection string, local development uses SQLite at `App_Data/repay.db`. Set `RePay__DataDirectory` to use a persistent mounted directory. **Render's free web-service filesystem is ephemeral: configure external PostgreSQL before relying on name retention across deploys/restarts.** Creating the schema in code alone does not provision or connect a hosted database.

This POC runs one application instance. Message serialization is local to that instance; a multi-instance deployment needs distributed coordination. Recent message deduplication prevents normal webhook repeats, but a crash after Meta accepts a reply and before the database save can still duplicate a reply. Add a durable inbox/outbox before production.

## Configuration and running

Use environment variables from `.env.example` (the app does not auto-load that file). Store all secrets in Render's environment secret settings, never in source or `appsettings.json`.

```bash
dotnet run --project RePay.WhatsAppPoc.csproj
```

For Render, use the included Dockerfile/Blueprint and configure PostgreSQL separately. Verify `/health`, then set Meta's Callback URL to `https://YOUR-HOST/webhooks/whatsapp` and Verify Token to the same value as `RePay__WhatsAppVerifyToken`. Subscribe the app/WABA to `messages`. Keep `RePay__MetaAccessToken` valid with WhatsApp messaging permissions; webhook verification alone does not test outbound authorization.

A failed webhook returns 503 so Meta can retry. Model failures use a short service-information fallback; failed delivery is not silently acknowledged as success. Database startup failures stop startup rather than resetting identities. Run `dotnet run --project tests/ConversationChecks.csproj` to check onboarding, restart persistence, deduplication, Saudi-only handling, and tool-free model requests without live messages.

Conversation updates: short contextual replies, future transfer/purchase examples with approval, and https://repay.sa on request. Users can say `غير اسمي إلى خالد` or `ابي اغير اسمي` then provide their preferred name. Name changes persist in the existing database. No browsing or transaction tools are enabled.
