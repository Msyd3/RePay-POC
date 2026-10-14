# Google Cloud Run migration

This prepares a deployment; it does not create paid Google Cloud resources or migrate traffic automatically.

## Preconditions

- A selected Google Cloud project, region, enabled billing/APIs, container registry and deploy permissions.
- External PostgreSQL (existing Neon can remain, or a planned Cloud SQL migration). Cloud Run deployments fail closed if PostgreSQL is missing; SQLite on its ephemeral filesystem would lose data.
- A dedicated runtime service account with access only to named Secret Manager secrets. Keep deploy/build permissions separate.
- Create secrets through approved secure tooling; pin secret versions. Do not type secret values into shell command history.
- The webhook must be reachable by Meta. The public endpoint authenticates POSTs with Meta HMAC; dashboard API separately authenticates its bearer secret.

## Image and rollout

The Dockerfile runs as the .NET non-root user. App reads Cloud Run's `PORT`; explicit port 10000 also works with Render. Build and push the image to the chosen registry, then substitute your approved values below:

```sh
gcloud run deploy repay-poc \
  --project YOUR_PROJECT \
  --region YOUR_REGION \
  --image YOUR_REGISTRY/repay-poc:YOUR_COMMIT \
  --service-account YOUR_RUNTIME_SERVICE_ACCOUNT \
  --port 10000 \
  --min 1 --max 1 --concurrency 1 --timeout 90 \
  --allow-unauthenticated \
  --set-secrets RePay__MetaAccessToken=repay-meta-token:1,RePay__MetaAppSecret=repay-meta-secret:1,RePay__WhatsAppVerifyToken=repay-verify-token:1,RePay__DatabaseConnectionString=repay-postgres:1,RePay__AnalyticsPassword=repay-dashboard:1,RePay__GroqApiKey=repay-groq:1 \
  --set-env-vars RePay__MetaPhoneNumberId=YOUR_PHONE_RESOURCE_ID
```

These flags intentionally keep the POC serialized and one warm instance for webhook latency; they incur costs and are not a replacement for distributed locks. Do not activate traffic on overlapping old/new revisions until the previous webhook handler is drained. Use the operations checklist before switching Meta's callback. Add Gemini's secret if using fallback.

## Verification and rollback

Require `/health` JSON, test the real Meta handshake and inbound reply, check failed-delivery statuses, test dashboard authorization, then record deployed commit/image and secret versions. Keep database unchanged during hosting-only migration. If migrating the database, plan backup, restore and cutover separately. To roll back, restore a healthy prior revision and its callback URL; preserve shared database state.

## Official references

- https://docs.cloud.google.com/run/docs/configuring
- https://docs.cloud.google.com/run/docs/configuring/services/secrets
- https://docs.cloud.google.com/sdk/gcloud/reference/run/deploy
