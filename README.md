# Yatra

Yatra is a small open-source sample for building asynchronous, agent-driven browser experiences with chat and dynamically defined forms.

A React application runs in the browser while an ASP.NET Core engine runs on the server. The browser sends user messages and form responses through HTTP. The engine delivers agent responses and form requests through Server-Sent Events (SSE). JSON-backed queues keep the two directions asynchronous and preserve unfinished work, pending forms, and conversation history across restarts.

The sample demonstrates one especially important idea: the browser never needs to know where a form response must go. It returns only the ULID of the original form message through `inReplyTo`; the engine uses that ULID to recover the private server-side return path and route the response to the original requester.

## What the sample includes

- React 19 and TypeScript browser UI
- ASP.NET Core engine targeting .NET 10
- HTTP POST for browser-to-engine messages
- SSE for engine-to-browser messages
- Separate persistent inbound and outbound queues
- JSON flat-file conversation message history
- ULID message and conversation identifiers
- Declarative dynamic forms rendered through approved built-in controls
- Server-side pending-reply correlation and return-path resolution
- One compile-time-selected sample orchestration adapter
- Deterministic local fake LLM and optional OpenAI integration
- Unit, integration, UI, routing, and end-to-end tests

Version 0.1 is an architectural sample, not a production agent platform.

## How it works

1. The browser creates a conversation ULID and opens its SSE connection.
2. The user sends text through the conversation message endpoint.
3. The engine places the message on its inbound queue.
4. The configured orchestration adapter processes the message.
5. Accepted browser messages and outbound server messages are appended to the local JSON conversation history.
6. The resulting text or form enters the outbound queue and reaches the browser through SSE.
7. If a form expects a response, the engine privately registers its return path before delivery.
8. The browser submits a new message containing `inReplyTo: <form-message-ulid>`.
9. The engine resolves the saved return path and sends the response to the original requester.

Internal return paths are never accepted from or exposed to the browser.

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) or later
- [Node.js 24](https://nodejs.org/) or later
- npm, included with Node.js

An API key is not required for the default fake-LLM configuration.

## Quick start

Clone the repository and restore the browser packages:

```powershell
git clone <repository-url>
cd Yatra
cd .\yatra-ui
npm install
cd ..
```

Start the server from the repository root:

```powershell
dotnet run --project .\src\Yatra.AppHost\Yatra.AppHost.csproj
```

In another terminal, start the browser application:

```powershell
cd .\yatra-ui
npm run dev
```

Open [http://localhost:5173](http://localhost:5173). The Vite development server proxies `/api` requests to the sample host at `http://localhost:5130`.

Wait until the connection indicator says **connected** before sending a new text message. Forms already displayed may still submit through HTTP while SSE reconnects. The browser stores the conversation ULID in `localStorage`, and when the stream connects the server replays stored JSON conversation messages for that conversation before live events.

## Try the sample

Enter any of these exact sample commands:

| Message | Result |
| --- | --- |
| `show contact form` | Displays the contact form |
| `request application access` | Displays the application-access form |
| `confirm an action` | Displays the confirmation form |
| `run sample skill` | Displays a sample skill form and processes the submitted inputs |
| Any other text | Returns a response from the configured LLM client |

Command matching is case-insensitive and ignores leading and trailing spaces.

You can open multiple forms and answer them in any order. Each reply is routed by its original form message ULID rather than by whichever form was displayed most recently.

## Build and test

Build and test the server projects:

```powershell
dotnet build .\Yatra.slnx
dotnet test .\Yatra.slnx
```

Test and build the React application:

```powershell
cd .\yatra-ui
npm ci
npm test
npm run build
```

## Configuration

Server settings are in `src/Yatra.AppHost/appsettings.json` and may be overridden by environment variables or command-line configuration.

For local AI work, edit the private constants `AiProvider`, `AiModel`, `AiApiKey`, and `AiTimeoutSeconds` in `src/Yatra.Orchestration/SampleOrchestrationAgentAdapter.cs`, then rebuild and restart the backend. Provider must be `Fake` or `OpenAI`. AI settings no longer come from appsettings or environment variables. Tests inject a fake client through `ILlmClient`.

These inline credentials are local-only and are embedded in compiled binaries. Remove the key before sharing source, archives, or builds.

Never place API keys in the React application or commit them to source control.

If the server uses a different URL, set `VITE_YATRA_API_BASE_URL` before starting or building the UI. Add the UI origin to `Yatra:AllowedCorsOrigins` when the browser and server use different origins.

`Yatra:StorageFolder` optionally sets the JSON file storage folder. When it is blank, Yatra uses `library` under the application host base directory.

See [Persistence and restart recovery](docs/Persistence.md) for stored files, browser drafts, atomic checkpoints, and adapter retry guarantees.

## Project map

```text
src/
  Yatra.Contracts/          Shared messages, forms, ULIDs, and routing records
  Yatra.AgentIntegration/   Agent-neutral adapter interfaces
  Yatra.Engine/             API, queues, SSE, pending replies, and routing
  Yatra.Orchestration/      Sample commands, forms, validation, and LLM clients
  Yatra.AppHost/            Composition root and runnable server
tests/
  Yatra.Contracts.Tests/
  Yatra.Engine.Tests/
  Yatra.Orchestration.Tests/
  Yatra.AppHost.Tests/
yatra-ui/                   React and TypeScript browser application
```

The engine does not reference the sample orchestration implementation. A different agent can be connected by implementing `IOrchestrationAgentAdapter` and changing the registration in the application composition root.

## Documentation

Start with the [documentation index](docs/README.md), or open:

- [Overview](docs/Yatra-Overview.md)
- [User guide](docs/User-Guide.md)
- [Architecture](docs/Architecture.md)
- [Message protocol](docs/Message-Protocol.md)
- [Agent adapter guide](docs/Agent-Adapter-Guide.md)
- [Developer guide](docs/Developer-Guide.md)
- [Persistence and recovery](docs/Persistence.md)
- [Security](docs/Security.md)
- [Testing](docs/Testing.md)
- [Troubleshooting](docs/Troubleshooting.md)
- [Contributing](CONTRIBUTING.md)
- [Changelog](CHANGELOG.md)

## Version 0.1 limitations

- Unfinished adapter execution may repeat after a crash; external side effects must deduplicate by message ID.
- Conversation messages are replayed from local JSON flat files, not a production event log.
- Authentication, authorization, durable conversation ownership, distributed messaging, and production observability are not implemented.
- The sample contains one orchestration requester; direct skill and tool execution are deferred.
- Token-by-token LLM streaming is deferred. Version 0.1 emits complete Yatra messages.
- Anonymous conversation ULIDs are suitable for a sample, not as production authorization.

## Author

Yatra was conceived, designed, and created by **Sivakumar Angarai**.

The project is released as open source so that developers and contributors can study, use, and extend its framework-neutral approach to agent-to-human interaction.

## License

Yatra is available under the [MIT License](LICENSE).
