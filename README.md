# RePay WhatsApp transfer POC

ASP.NET Core (.NET 10) chatbot using WhatsApp Cloud API, Groq/Gemini, PostgreSQL (Neon) or SQLite, and Render Docker hosting.

## Conversation

- Arabic input gets Arabic replies; English input gets English replies. Numeric replies and contact cards retain the conversation language.
- Transfers only: payment, shopping and product search are removed.
- Start with `حول لمحمد ٢٠ ريال`, `send Mohammed 20 SAR`, a Saudi mobile number, or a shared WhatsApp contact. Shared contact followed by `حوله ٢٠` continues the same draft.
- Collect Saudi mobile (10 local digits starting 05), amount in SAR (minimum 1), optional recipient name/note and sender first/last name. A valid mobile skips the recipient-name question. Common Arabic and English number words are accepted, including compound amounts. Incomplete local mobiles report missing digits. International +966 equivalents remain supported for contact cards. Ask only missing fields. Never read an address book automatically.
- Sender name can be edited in Arabic or English while keeping the transfer draft. Changing recipient name clears the previous recipient phone. Every edit invalidates the prior review button.
- A personalized welcome asks unknown customers for their full name before showing quick-transfer instructions. Recipient names resolve only from up to 20 contacts explicitly shared in this customer’s conversation; ambiguous matches ask for the mobile number. The WhatsApp API does not expose the customer’s address book.
- Review uses a native WhatsApp reply button, not a displayed URL. Button IDs identify a particular draft revision and expire after three minutes. Tapping it currently explains that the confirmation page will be implemented later. No Apple Pay, financial execution, recipient SMS, OTP, bank linkage or withdrawal is implemented.
- Webhooks reject old timestamps and deduplicate message IDs. Generated text/button state is persisted before sending so retries reuse the same response. Per-user locks serialize events on this single-instance POC.
- Phone numbers use English digits with left-to-right isolation in the transfer summary and are redacted from history and withheld from model context. Recipient phone is retained only in the persisted draft for future review integration.

## Configuration

Use the existing `RePay` configuration/environment variables in `Services/RePayOptions.cs`. Do not commit credentials. Render's free instance can sleep when idle. PostgreSQL is recommended for persistence across redeploys; local SQLite requires persistent storage.

## Checks

`dotnet run --project tests/ConversationChecks.csproj`

Checks cover Arabic/English transfers, shared contacts, digit normalization, sender name collection and edits, recipient phone isolation, optional notes, cancelled requests, stale review buttons, review expiry, actual Meta button payloads, stale/duplicate webhooks, send retries, and persistence. Semantic intent tests mock the model; live natural-language behavior still depends on the configured provider.
