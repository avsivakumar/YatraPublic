# Testing

Yatra uses .NET tests for contracts, engine behavior, orchestration, and end-to-end hosting, plus Vitest and Testing Library for the React UI.

## Run the suite

From the repository root:

```powershell
dotnet build .\Yatra.slnx
dotnet test .\Yatra.slnx
```

Then run the UI checks:

```powershell
cd .\yatra-ui
npm ci
npm test
npm run build
```

`npm run build` performs TypeScript checking before the Vite production build.

## Test organization

| Project or file | Primary responsibility |
| --- | --- |
| `Yatra.Contracts.Tests` | Serialization, ULIDs, message and form contracts |
| `Yatra.Engine.Tests` | HTTP acceptance, queues, routing, pending replies, SSE, persistence, recovery, options and dependency boundaries |
| `Yatra.Orchestration.Tests` | Commands, form definitions, exhaustive server validation, adapter behavior and LLM integration boundary |
| `Yatra.AppHost.Tests` | Hosted browser-to-engine-to-adapter flows and restart scenarios |
| `App.test.tsx` | Conversation behavior, lifecycle outcomes, clear-messages behavior and integration of UI pieces |
| `DynamicForm.test.tsx` | Rendering, validation, retry identity, state transitions and accessibility behavior |
| `api.test.ts` | HTTP acknowledgement and error handling |
| `formValidation.test.ts` | Form-definition and submitted-value rules |
| `MessageList.test.tsx` | Message and unsupported-content rendering |
| `ulid.test.ts` | ULID generation and conversation identity persistence |

## Required coverage for changes

- Contract changes require serialization and compatibility tests.
- New field types require definition, UI rendering, client validation, server validation, response serialization, unsupported-version, and accessibility tests.
- Queue or persistence changes require failure injection and restart recovery tests.
- SSE changes require reconnect, cursor, replay, duplicate, heartbeat, and exhaustion tests.
- Form lifecycle changes require early/late HTTP-versus-SSE race tests.
- Adapter changes require correlation, validation, cancellation, retry, and external-side-effect idempotency consideration.

Tests must not call a paid or nondeterministic model service. Inject `ILlmClient` or use the Fake provider.

## Manual smoke test

1. Start the engine and UI with the Fake provider.
2. Send ordinary text and each sample command.
3. Trigger required-field errors and confirm focus moves to the first invalid field.
4. Submit a form and observe `submitting`, `submitted`, then the correlated terminal outcome.
5. Interrupt SSE and confirm bounded reconnection and replay without duplicate display.
6. Reload with an unfinished draft and confirm restoration.
7. Restart the engine with pending work and confirm recovery.
8. Clear displayed messages, reload, and confirm cleared history remains hidden while new messages appear.
9. Inspect desktop and narrow mobile layouts with keyboard-only navigation.

## Verified Smart 8 baseline

The reviewed Smart 8 source reported 210 passing .NET tests. In this workspace, 51 frontend tests passed and the TypeScript/Vite production build succeeded. Re-run the complete suite in an environment with the .NET 10 SDK before release.
