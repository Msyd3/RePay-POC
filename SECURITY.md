# Security handover

Implemented: required Meta HMAC signature, fixed-time secret comparison, refusal of empty verify token, 1 MiB request limit, dashboard bearer secret, request rate limiting for analytics, no-store analytics responses, parameterized SQL, response hardening headers, non-root container, and secret/data exclusion patterns.

Do not commit real `.env`, database files, tokens, PINs, service-account keys or customer data. Use Secret Manager/host secret configuration and least-privilege identities. Grant database access only to the backend and operators. Rotate exposed credentials; repository ignore rules do not remove secrets already committed in Git history.

The dashboard's shared bearer secret is a POC mechanism. Use SSO/IAP or equivalent identity, authorization and audit for production. Rate limiting is currently process-local. TLS terminates at the host; PostgreSQL must verify its server certificate. Stored conversation data is not encrypted by application code; select storage encryption and access controls on the host/database.

Signed webhook events are queued and processed by a hosted worker. With PostgreSQL, events are persisted to the database inbox before HTTP 200; failures retry with exponential backoff up to eight attempts, then dead-letter rows retain only the event hash and exception type. Pending payloads contain customer message data until processing succeeds or retries are exhausted. Without PostgreSQL, a bounded in-memory queue is used; a process restart can lose queued events, so this mode is suitable only for the current POC. Delivery statuses are not persisted. The worker and per-user locks are single-instance; before multi-instance production use, add distributed claiming and distributed user locks. Summary counters are not settlement records.

Security checks here are targeted engineering checks, not a penetration test or guarantee of complete security. Report vulnerabilities privately to the repository maintainer; do not include credentials or personal data in public issues.
