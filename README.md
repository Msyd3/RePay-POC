# RePay POC

Let your agents prepare purchases on your behalf. Payment credentials remain private, and every purchase stays subject to your approval.

RePay is an ASP.NET Core proof of concept for agent-assisted commerce. It accepts a customer request through a configured channel, searches for products, presents choices, and lets a guarded browser service prepare a basket and checkout summary. It always stops before payment: this repository contains no payment endpoint, credential handling, or pay() operation.

## Product flow

Customer request → product search → numbered choices → customer selection → guarded checkout preparation → approval summary

The agent may discover products and prepare a checkout, but it cannot submit a purchase or access card data, passwords, or one-time codes.

## Architecture

- **Channel adapter** receives signed customer messages and sends responses.
- **Agent provider** searches and recommends products. The primary provider is Groq through its OpenAI-compatible API.
- **Browser agent** prepares a cart and checkout summary through one guarded operation.

## Configure secrets

Keep secrets outside source control:

```bash
export RePay__GroqApiKey='Groq API key'
export RePay__GroqModel='groq/compound-mini'
export RePay__BrowserAgentBaseUrl='https://internal-browser-agent.example'
```

The browser service must accept `POST /prepare-checkout` with `stopBeforePayment: true`. It must use an isolated profile, restrict itself to approved merchant domains, reject every payment action, and never collect payment credentials.

## Agent providers

The current implementation uses Groq. The provider boundary can later use Gemini, OpenRouter, or Ollama with an open model on your own server.

## Production follow-up

Use a durable queue, persist conversation and approval state, add idempotency, and require an explicit user approval outside the agent before every purchase.
