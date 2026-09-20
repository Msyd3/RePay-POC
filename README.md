# RePay WhatsApp shopping POC

An ASP.NET Core webhook for a WhatsApp shopping assistant. It verifies Meta callbacks, validates the signed payload, asks an OpenAI model to search and present options, sends the result to WhatsApp, and accepts a choice. A separate browser-agent service can add that choice to a cart and prepare checkout. It is explicitly sent `stopBeforePayment: true`; this project has no payment endpoint, payment tool, or call to `pay()`.

## Flow

`WhatsApp → Meta webhook → OpenAI web search → numbered options → user replies 1–3 → browser agent prepares checkout → WhatsApp summary`

Keep this POC separate from the current RePay backend. Its `MetaWhatsAppClient`, `OpenAiAgentClient`, and `SafeBrowserAgent` classes are intended as the integration boundary when moving it into the main solution.

## Configure secrets

Do not add secrets to `appsettings.json`. Copy the values from `.env.example` into your deployment secret store or export them locally. ASP.NET's double-underscore configuration mapping is used:

```bash
export RePay__WhatsAppVerifyToken='a-long-random-value'
export RePay__MetaAppSecret='Meta app secret'
export RePay__MetaAccessToken='system-user access token'
export RePay__MetaPhoneNumberId='phone number id'
export RePay__OpenAiApiKey='OpenAI API key'
export RePay__OpenAiModel='gpt-5-mini'
export RePay__BrowserAgentBaseUrl='https://internal-browser-agent.example'
```

`BROWSER_AGENT_BASE_URL` is represented by `RePay__BrowserAgentBaseUrl` above. Its service must accept `POST /prepare-checkout` and enforce `stopBeforePayment`. Use an isolated browser profile and allow-listed merchant domains; never give it card data or a payment action.

## Run

```bash
dotnet run --project RePay.WhatsAppPoc
```

Expose the local app through an HTTPS tunnel, for example using your approved tunnel provider. The public callback is:

`https://YOUR-DOMAIN/webhooks/whatsapp`

Check `https://YOUR-DOMAIN/health` before configuring Meta.

## Browser-agent contract

The browser agent is deliberately a separate internal service. Configure its base address in `RePay__BrowserAgentBaseUrl`; RePay then calls:

```http
POST /prepare-checkout
Content-Type: application/json

{ "choice": "1", "stopBeforePayment": true }
```

The service must use an isolated browser profile, open only your approved merchant domains, add the chosen item to the basket, and return a short checkout summary. It must reject every payment action and never collect card data, passwords, or OTPs. This is the boundary that lets the current POC connect to the future RePay browser service without adding purchase authority to the WhatsApp backend.

## OpenAI API key

Create a project-scoped key in the [OpenAI API keys page](https://platform.openai.com/api-keys): choose the RePay project (or create one), select **Create new secret key**, and save it immediately. Use a project key with a spend limit rather than a personal shared key. Set it only in the runtime secret store as `RePay__OpenAiApiKey`; never paste it into source code or commit it. OpenAI documents project key management in its [API project guide](https://help.openai.com/en/articles/9186755).

## Meta configuration

1. In Meta for Developers, open the app's **WhatsApp > Configuration** page and edit the webhook.
2. Enter the public callback URL above and the exact same value used for `RePay__WhatsAppVerifyToken` as **Verify token**. Meta sends `hub.challenge`; the app returns it only when the token matches.
3. Subscribe to the `messages` webhook field and save.
4. Send a WhatsApp message to the test number. Meta signs every POST using `X-Hub-Signature-256`; the app rejects messages whose HMAC does not match `RePay__MetaAppSecret`.

The OpenAI Responses API supports a built-in web-search tool and custom function tools. This POC uses web search for discovery and reserves the guarded checkout operation for the browser service. See the official [OpenAI web-search guide](https://developers.openai.com/api/docs/guides/tools-web-search) and [function-calling guide](https://developers.openai.com/api/docs/guides/function-calling).

## Production follow-up

Replace `ConversationStore` with a database keyed by WhatsApp message ID, store merchant and cart state, respond to Meta quickly by queueing work, validate tenant authorization, and keep payment initiation in a separately reviewed RePay workflow.
