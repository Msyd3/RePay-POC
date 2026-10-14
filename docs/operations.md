# Configuration and operations

Read `.env.example` and `Configuration/RePayOptions.cs`. Configuration uses `RePay__` environment keys.

| Key | Purpose |
| --- | --- |
| `RePay__MetaPhoneNumberId` | Meta resource ID, not mobile digits |
| `RePay__MetaAccessToken` | Token scoped to the correct app and messaging account |
| `RePay__MetaAppSecret` | Validates webhook HMAC |
| `RePay__WhatsAppVerifyToken` | Random secret for Meta callback handshake |
| `RePay__DatabaseConnectionString` | Durable PostgreSQL connection with certificate validation |
| `RePay__AnalyticsPassword` | Random dashboard bearer secret; recommend 32+ characters |
| `RePay__GroqApiKey`, `RePay__GeminiApiKey` | AI provider credentials; Gemini is fallback |
| `PORT` | Optional host-injected listening port; default Docker port 10000 |

`RePay__AnalyticsPhone` is no longer accepted as dashboard authentication. A phone number is public identity, not a secret. Configure AnalyticsPassword before deploying this change. Blank password denies dashboard access.

## Safe rollout

1. Back up PostgreSQL and keep the existing service available until verification completes.
2. Configure secrets in the host, never in GitHub or deploy command text.
3. Deploy the image and require `/health` to return JSON `{"status":"ok"}`. This is process liveness, not an external provider readiness check; initialization currently opens the database before serving.
4. Point Meta callback to `https://YOUR_HOST/webhooks/whatsapp`, use the same verify token, and keep the `messages` field and app/account subscription enabled.
5. Send an inbound message to the business number. Confirm signed webhook processing, one reply, summary, name/amount edits and Arabic/English behavior.
6. Confirm the dashboard is unauthorized without its secret and works with it. Check actual delivered/failed webhook statuses; do not treat a Meta message ID as proof of delivery.
7. Switch traffic only after these checks. Roll back to the old healthy host by restoring its callback and revision if needed.

## Troubleshooting

- Render `Service Unavailable` before a build starts: inspect host status and event log; changing conversation code cannot fix a platform outage.
- No inbound reply: confirm live `/health`, callback URL, signature secret, correct WABA subscription, current phone resource ID/token and provider/database connectivity.
- HTTP 401: signature or dashboard authentication failed. Never disable signature verification as a workaround.
- Meta accepts send but nothing arrives: inspect delivery error in webhook status. Plain text needs an open customer service window; otherwise use an approved template under current Meta rules.
- The system-user token created during setup expires after 60 days. Rotate before expiry and test; do not put the token in URLs, tickets or screenshots.

Logs and database dumps can contain personal data. Restrict access and retain only as required; no automatic retention is implemented.
