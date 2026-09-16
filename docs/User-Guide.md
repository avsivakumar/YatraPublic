# Yatra User Guide

## 1. About this guide

This guide explains how to install, run, and use the Yatra version 0.1 sample. It is intended for someone evaluating the application from the browser or demonstrating its conversation and dynamic-form behavior.

For implementation details such as queue internals, return-path storage, agent adapters, and wire contracts, refer to the separate technical documentation.

## 2. What Yatra demonstrates

Yatra provides a browser conversation in which an AI agent can return ordinary text or request structured information through a dynamic form.

The browser does not contain hard-coded contact, access-request, or confirmation screens. It receives declarative form definitions and maps their approved field types to trusted React components.

When a form is answered, the browser identifies the original form using its message ULID. The server privately determines which agent request should receive the answer. Internal routing information never travels from the browser.

## 3. System requirements

Install the following software:

- .NET SDK 10 or later
- Node.js 24 or later
- npm
- A current browser with `EventSource`, local storage, session storage, and Web Crypto support

Chrome, Edge, Firefox, and Safari current releases provide the browser capabilities used by the sample.

The default fake LLM works without credentials or an external service.

## 4. Starting the application

Yatra has two processes during local development:

1. The ASP.NET Core server
2. The Vite React development server

### 4.1 Restore the UI packages

From the repository root:

```powershell
cd .\yatra-ui
npm install
cd ..
```

Use `npm ci` instead when you want a clean, repeatable installation from `package-lock.json`.

### 4.2 Start the server

From the repository root:

```powershell
dotnet run --project .\src\Yatra.AppHost\Yatra.AppHost.csproj
```

The standard HTTP development address is:

```text
http://localhost:5130
```

Keep this terminal open while using the application.

### 4.3 Start the React UI

Open a second terminal:

```powershell
cd .\yatra-ui
npm run dev
```

Open the address shown by Vite, normally:

```text
http://localhost:5173
```

The development server forwards `/api` calls to the ASP.NET Core server.

## 5. Understanding the screen

The main screen contains:

- The **conversation identifier**, a ULID created for the current browser tab
- A **connection indicator**
- The chronological **conversation history**
- Inline **dynamic-form cards**
- The **message composer** and Send button

The conversation identifier is stored in `localStorage`. Refreshing the page or opening another tab on the same origin reuses it until browser site data is cleared.

### Connection states

| State | Meaning |
| --- | --- |
| `connecting` | The browser is opening its SSE stream |
| `connected` | The server can deliver future messages to this tab |
| `error` | The stream encountered a transport problem and may reconnect automatically |
| `disconnected` | The EventSource connection is closed |

New text messages require a connected stream. Forms already displayed remain available for HTTP submission during SSE recovery; the correlated outcome appears after the stream reconnects.

## 6. Sending a normal question

1. Wait for the connection indicator to display `connected`.
2. Enter a question or statement in the composer.
3. Select the Send button.
4. Your message appears immediately in the conversation.
5. The server processes it asynchronously and returns an agent message.

With the default fake provider, Yatra returns a deterministic sample response. Selecting the OpenAI provider causes ordinary text to be sent through the configured server-side LLM client.

The UI limits a message to 8,000 characters and ignores an empty or whitespace-only submission.

## 7. Requesting sample forms

Version 0.1 recognizes three deterministic commands.

### Contact form

Enter:

```text
show contact form
```

The returned form asks for:

- Name
- Email
- Subject
- Message

### Application-access form

Enter:

```text
request application access
```

The returned form asks for:

- Application
- Role
- Business justification
- Access end date

### Confirmation form

Enter:

```text
confirm an action
```

The returned form presents a required confirmation checkbox and Confirm and Cancel actions.

The commands are case-insensitive. Spaces before or after the command are ignored.

## 8. Working with dynamic forms

Yatra version 0.1 supports these field types:

| Field type | Browser control |
| --- | --- |
| `text` | Single-line text input |
| `textarea` | Multi-line text input |
| `number` | Numeric input |
| `date` | Date selector |
| `select` | Approved list of choices |
| `checkbox` | Boolean confirmation |
| `datetime` | Local date and time |
| `multiselect` | Multiple approved choices |
| `display` | Display-only text |
| `hidden` | Supplied non-editable value |

The browser renders only this approved registry. An unsupported form is displayed as **Unsupported form** rather than interpreted as HTML or executable code.

### Client-side validation

Before submission, the UI can enforce:

- Required fields
- Minimum and maximum text lengths
- Text patterns
- Minimum and maximum numeric values
- Allowed select options
- Required confirmation checkboxes

Validation messages appear next to the affected fields. Correct the values and submit again.

The server repeats authoritative validation. Browser validation improves usability but is not treated as a security boundary.

## 9. Form submission lifecycle

Form submission is asynchronous.

1. Select Submit or the form-specific submit action.
2. Yatra validates the visible values.
3. The browser generates a new response-message ULID.
4. It sends the original form-message ULID as `inReplyTo`.
5. After a valid HTTP acknowledgement, the form displays **submitted** and becomes read-only while processing continues. Only a correlated agent confirmation changes it to **completed**; a retryable asynchronous error reopens it.
6. Later agent messages describe the business result; retryable rejection reopens the form.

For cancellation:

1. Select Cancel.
2. The browser sends the cancellation with an empty value set.
3. The form waits for a valid HTTP acknowledgement.
4. After acknowledgement, it displays **submitted** until the correlated agent confirmation changes it to **cancelled**.

The HTTP `202 Accepted` response means that the engine queued the message. It does not mean that the original requester has finished processing it.

## 10. Failures and resubmission

### HTTP submission failure

If the server does not accept the POST request:

- The form remains editable.
- The entered values remain in place.
- The form displays **Response was not accepted.**
- You can try again after correcting the connection or server problem.

### Retryable asynchronous failure

The server may accept the message but later be unable to deliver it to the requester. When the resulting SSE error is retryable:

- The form reopens.
- Previously entered values are preserved.
- The error message is displayed.
- You can submit again.

After explicit asynchronous rejection, a new attempt uses a new response-message ULID and references the previous response. A transport retry after timeout or lost acknowledgement reuses the original ULID and content. Both reference the original form through `inReplyTo`.

### Terminal failure

For a non-retryable error, such as a duplicate or expired interaction:

- The form remains closed.
- The terminal error is shown in the conversation.
- The form cannot be resubmitted.

This distinction prevents accidental duplicate handling while allowing recovery when the server has released the pending request.

## 11. Using more than one form

You may request multiple forms before answering them. Submit the forms in any order.

For example:

1. Enter `show contact form`.
2. Enter `request application access`.
3. Submit the access-request form first.
4. Submit the contact form second.

Each form response contains the ULID of its own form request. The engine therefore returns each response to the correct requesting interaction rather than assuming the newest form is the intended destination.

This reverse-order behavior is one of the principal demonstrations in the sample.

## 12. Reconnection behavior

The browser reconnects with bounded delays after SSE interruption. Forms stay visible with their values intact and can submit through HTTP. After recovery is exhausted, use **Retry connection**.

After reconnection, the browser resumes from its last event ID using stored history. Replays are deduplicated and acknowledged forms stay locked.

Queued work and pending forms now recover after restart. External adapters must deduplicate side effects if a crash interrupts processing; see [Persistence](Persistence.md).

### Clearing displayed messages

**Clear all messages** hides the current conversation history in this browser, including after reload and server replay. It does not delete server history or pending work. Messages arriving after the clear operation remain visible.

## 13. Selecting the LLM provider

### Fake provider

For demonstrations and local work, set the private agent constant in `SampleOrchestrationAgentAdapter.cs`:

```csharp
private const string AiProvider = "Fake";
```

No API key is needed.

### OpenAI provider

To enable the included OpenAI client, edit the private constants in that same agent file, not the browser:

```csharp
private const string AiProvider = "OpenAI";
private const string AiModel = "<supported-model-name>";
private const string AiApiKey = "<your-api-key>";
private const int AiTimeoutSeconds = 30;
```

Then rebuild and restart the ASP.NET Core host. Appsettings and environment variables no longer select the AI provider or credentials. Inline keys are local-only: remove them before sharing source, archives, or binaries.

If the provider is unavailable or times out, the conversation receives a friendly retryable `llm_unavailable` error. Credentials and internal exception details are not sent to React.

Model availability and API charges depend on the account and model selected. The sample does not enforce budgets or usage limits.

## 14. Using a different server address

The Vite proxy expects the engine at `http://localhost:5130`. To call another engine origin, set:

```powershell
$env:VITE_YATRA_API_BASE_URL = "https://your-server.example"
npm run dev
```

The server must allow the UI origin in `Yatra:AllowedCorsOrigins`. For example:

```json
{
  "Yatra": {
    "AllowedCorsOrigins": [
      "http://localhost:5173"
    ]
  }
}
```

Do not use unrestricted development CORS settings as a substitute for production authorization.

## 15. Checking the installation

Run the server test suite from the repository root:

```powershell
dotnet test .\Yatra.slnx
```

Run the UI suite and production build:

```powershell
cd .\yatra-ui
npm ci
npm test
npm run build
```

The AppHost end-to-end tests exercise:

- Ordinary fake-LLM questions
- Contact-form submission
- Reverse-order responses to multiple forms
- Duplicate-response rejection
- Cross-conversation rejection
- Future-message delivery after SSE reconnection

## 16. Current limitations

Yatra version 0.1 intentionally does not provide:

- Distributed queues or multi-instance coordination
- A production database-backed message store
- Authentication or authorization
- Production conversation ownership or tenant isolation
- A skill loader or tool-execution runtime
- Direct skill-generated forms
- Token-by-token LLM streaming
- Production deployment, monitoring, or scaling guidance

The application uses anonymous conversation ULIDs to demonstrate routing. Possession of a conversation identifier must not be treated as authorization in a production system.

## 17. Stopping and restarting

Stop the UI and server with `Ctrl+C` in their respective terminals.

Restarting the server restores:

- Inbound and outbound queue contents
- Pending form-return routes
- Completed, cancelled, and expired reply records

Keep the configured storage folder when restarting. The browser reconnects to the same conversation; drafts and retry identities also survive page reloads in browser local storage. Live connections are recreated automatically. Forms retain their original expiry times.

## 18. Getting further help

For common startup, CORS, SSE, LLM, and form-response problems, consult [Troubleshooting](Troubleshooting.md).

When reporting an issue, include:

- Operating system
- .NET and Node.js versions
- Whether the fake or OpenAI provider was selected
- Browser name and version
- Server and UI console output with secrets removed
- Steps needed to reproduce the behavior

Never include API keys, complete sensitive form values, or private LLM content in an issue report.
