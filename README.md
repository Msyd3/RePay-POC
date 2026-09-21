# RePay POC

Let your agents prepare purchases on your behalf. Payment credentials remain private, and every purchase stays subject to your approval.

RePay is an ASP.NET Core proof of concept for agent-assisted commerce. It accepts a customer request through a configured messaging channel, searches for products, presents choices, and lets a guarded browser service prepare a basket and checkout summary. It always stops before payment: this repository contains no payment endpoint, payment credential handling, or `pay()` operation.

## Product flow

`Customer request → product search → numbered choices → customer selection → guarded checkout preparation → approval summary`

RePay keeps the customer in control. The agent may discover products and prepare a checkout, but it cannot submit a purchase or access card data, passwords, or one-time codes.

## Architecture

The project separates three replaceable boundaries:

- **Channel adapter** receives signed customer messages and sends responses.
- **Agent router** tries Groq first, then Gemini automatically if Groq is unavailable. More providers can be added without changing the channel or checkout flow.
- **Browser agent** prepares a cart and checkout summary through one guarded operation.

This separation is intended for later integration into the main RePay backend.

## Configure secrets

Do not add secrets to `appsettings.json`. Copy the values from `.env.example` into a deployment secret store or export them locally. ASP.NET's double-underscore configuration mapping is used:

```bash
export RePay__WhatsAppVerifyToken='a-long-random-value'
export RePay__MetaAppSecret='channel app secret'
export RePay__MetaAccessToken='channel access token'
export RePay__MetaPhoneNumberId='channel sender id'
export RePay__GroqApiKey='Groq API key'
export RePay__GroqModel='groq/compound-mini'
export RePay__GeminiApiKey='Gemini API key'
export RePay__GeminiModel='gemini-2.5-flash'
export RePay__BrowserAgentBaseUrl='https://internal-browser-agent.example'
```

`RePay__BrowserAgentBaseUrl` must point to a trusted internal service that accepts `POST /prepare-checkout`. Keep its browser profile isolated and restrict it to approved merchant domains.

## Run

```bash
dotnet run --project RePay.WhatsAppPoc
```

Expose the app through HTTPS and confirm `https://YOUR-DOMAIN/health` before configuring the channel callback at `https://YOUR-DOMAIN/webhooks/whatsapp`.

## Deploy for testing

The included `Dockerfile` and `render.yaml` can run the POC on Render's free web-service plan. Connect this repository in Render, create the service from the Blueprint, and enter the secret values when prompted. After deployment, update the Meta callback URL to:

```text
https://YOUR-RENDER-SERVICE.onrender.com/webhooks/whatsapp
```

The local `App_Data` user store is ephemeral on free hosting. Replace it with the RePay database before using the service beyond a short POC.

## Browser-agent contract

RePay calls the browser agent with one guarded request:

```http
POST /prepare-checkout
Content-Type: application/json

{ "choice": "1", "stopBeforePayment": true }
```

The browser agent must add the chosen item to a basket and return a concise checkout summary. It must reject all payment actions and never collect or transmit payment credentials. A separate, reviewed RePay purchase-approval workflow should own any future payment step.

## Agent providers

The starter implementation uses Groq first and Gemini as fallback. Both implement `IAgentProvider`, so a third provider or a specialised agent can be added without changing message handling:

- Gemini for hosted models and search grounding.
- OpenRouter for provider fallback.
- Ollama with an open model on your own server for fixed infrastructure cost and greater control.

Store the chosen provider's API key only in runtime secrets. Never commit it.

## Production follow-up

- Use a durable queue so callback acknowledgements are immediate.
- Store conversation, merchant, cart, and approval state in a database.
- Add idempotency keyed by message ID.
- Require an explicit approval step outside the agent before every purchase.
- Use a stable HTTPS deployment rather than a temporary development tunnel.
