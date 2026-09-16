# Developer Guide

## Dynamic form reliability checks

Use [Message Protocol](Message-Protocol.md) as the canonical contract. UI definition/value validation lives in `yatra-ui/src/formValidation.ts`; trusted rendering lives in `components/DynamicForm.tsx`. Add contract and rendering tests when extending fields. Unknown fields and inappropriate constraints fail the whole definition.

Run `npm test` and `npm run build` in `yatra-ui`, and `dotnet test Yatra.slnx` at the root. Reliability coverage includes acknowledgement checking, lost-ack identity reuse, concurrent replay, conflicting content, cursor replay, bounded SSE recovery, and malformed definitions. Diagnostics must exclude entered field values. Sample adapter business validation remains separate from generic client validation.

This guide covers the local workflow for Yatra v0.1.

## Prerequisites

- .NET SDK 10.0 or later.
- Node.js 24 or later.
- PowerShell on Windows for the documented commands.

## Restore and Build

From the repository root:

```powershell
dotnet build .\Yatra.slnx
```

For the UI:

```powershell
cd .\yatra-ui
npm ci
npm run build
```

## Test

Run all .NET tests:

```powershell
dotnet test .\Yatra.slnx
```

Run UI tests:

```powershell
cd .\yatra-ui
npm test
```

The test suite covers contracts, queues, routing, pending replies, SSE behavior, sample orchestration behavior, UI form states, and AppHost end-to-end flows.

## Run Locally

Start the backend:

```powershell
dotnet run --project .\src\Yatra.AppHost\Yatra.AppHost.csproj
```

Start the UI in another terminal:

```powershell
cd .\yatra-ui
npm run dev
```

The UI runs at `http://localhost:5173`. Vite proxies `/api` to the sample backend at `http://localhost:5130`.

If the backend uses a different origin, set:

```powershell
$env:VITE_YATRA_API_BASE_URL = "http://localhost:5131"
```

## Useful Sample Prompts

In the UI, try:

- `hello`
- `show contact form`
- `request application access`
- `confirm an action`
- `run sample skill`

The sample adapter returns fake LLM text by default, renders deterministic forms for the form commands, and processes the sample skill form after submission.

## Configuration

Backend settings live in `src\Yatra.AppHost\appsettings.json` and may be overridden by environment variables or command-line configuration.

Important settings:

- `Yatra:InboundQueueCapacity`
- `Yatra:OutboundQueueCapacity`
- `Yatra:StreamChannelCapacity`
- `Yatra:PendingReplyLifetimeMinutes`
- `Yatra:PendingReplyTerminalRetentionMinutes`
- `Yatra:PendingReplyCleanupSeconds`
- `Yatra:SseHeartbeatSeconds`
- `Yatra:AllowedCorsOrigins`
- `Yatra:StorageFolder`

Queue capacities and time intervals must be greater than zero. AI settings are private constants in `SampleOrchestrationAgentAdapter.cs`: `AiProvider` (`Fake` or `OpenAI`), `AiModel`, `AiApiKey`, and `AiTimeoutSeconds`. They no longer use host configuration.

When `Yatra:StorageFolder` is blank, Yatra stores JSON conversation files under `library` in the application host base directory.

The browser stores the current conversation ULID in `localStorage`. Clearing browser site data starts a new conversation and therefore reads a different history file.

To use OpenAI, edit those constants locally, then rebuild and restart:

```powershell
dotnet run --project .\src\Yatra.AppHost\Yatra.AppHost.csproj
```

Do not commit or share the populated key, source archives, or compiled binaries containing it. Tests inject `FakeLlmClient` through `ILlmClient` rather than overriding environment variables.

## Architecture Rules

- Keep reusable contracts in `Yatra.Contracts`.
- Keep integration interfaces in `Yatra.AgentIntegration`.
- Keep engine runtime behavior in `Yatra.Engine`.
- Keep concrete sample agent behavior in `Yatra.Orchestration`.
- Keep composition in `Yatra.AppHost`.
- Do not reference `Yatra.Orchestration` from `Yatra.Engine`.

There is an architecture guard test for the last rule.

## Adding a Form

1. Add or update the form definition in the orchestration package.
2. Give the form a stable `formId` and `formVersion`.
3. Add server-side validation for all fields.
4. Ensure the published `form.request` has `ExpectsReply = true`.
5. Publish it with a nonblank `ReturnPath`.
6. Add tests for valid submit, cancel, invalid values, duplicate response, and retryable failure when relevant.

Browser-side validation is for usability only. Server validation is authoritative.

## Packaging

The repository includes a root `.gitignore` and MIT `LICENSE`.

When creating a source package, exclude generated artifacts and dependencies:

- `bin`
- `obj`
- `TestResults`
- `node_modules`
- `dist`
- `*.tsbuildinfo`
- archives and binaries

The smart zip used during development includes readable source and documentation files only.

## Current Limits

Yatra uses JSON-backed inbox/outbox checkpoints, pending replies, and conversation history. [Persistence](Persistence.md) describes restart recovery, storage ownership, browser state, and at-least-once adapter execution. Authentication, authorization, and distributed event storage remain outside the sample.
