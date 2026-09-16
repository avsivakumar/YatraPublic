# Yatra Architecture

## Dynamic form reliability

Form drafts are independent of HTTP request state. Unsupported definitions fail before rendering. One guarded handler covers keyboard and pointer submission. Validated HTTP acknowledgements lock forms; correlated retryable application errors permit correction. BrowserSubmissionGate serializes acceptance and compares persisted content, avoiding re-enqueueing retries and returning 409 for conflicts.

SSE transport owns bounded reconnect timers and closes old streams before reconnecting. It resumes from the last event ID and deduplicates before delivering events. Conversation rendering owns the connection status and retry control and keeps forms mounted. Form HTTP submission remains available while SSE reconnects.

The engine now uses one atomic JSON operational snapshot for durable inbox/outbox checkpoints and pending reply routing/status. Conversation history is projected before SSE delivery. Startup recovers unfinished work and interrupted claims. See [Persistence](Persistence.md) for transaction and external-adapter boundaries.

Yatra is a small architectural sample with contracts, JSON-backed queues and pending replies, JSON conversation history, one sample orchestration adapter, and a React UI.

## Goals

- Keep `Yatra.Engine` reusable and agent-neutral.
- Let browser messages enter through a trusted server boundary.
- Route form replies back to the requester that originally asked for them.
- Demonstrate SSE delivery and local JSON replay without pretending it is production event storage.
- Provide enough tests for the important routing and correlation guarantees.

## Projects

- `Yatra.Contracts`: shared message, form, error, routing, validation, and ULID contracts.
- `Yatra.AgentIntegration`: interfaces used by agents and adapters.
- `Yatra.Engine`: queueing, pending-reply registry, routing workers, SSE stream hub, and HTTP endpoints.
- `Yatra.Orchestration`: sample adapter, sample forms, form-response validator, fake LLM client, and OpenAI SDK-backed LLM client.
- `Yatra.AppHost`: composition root that wires the reusable engine to the sample orchestration adapter.
- `yatra-ui`: React/Vite browser UI.

`Yatra.Engine` must not reference `Yatra.Orchestration`. The dependency direction is guarded by an automated test.

## Runtime Flow

1. Browser opens an SSE stream for a conversation:
   `GET /api/conversations/{conversationId}/events`.
2. Browser submits `user.text` or `form.response`:
   `POST /api/conversations/{conversationId}/messages`.
3. The engine constructs the trusted internal message by affixing:
   `Source = ui:current-conversation`,
   `Destination = engine:inbound`,
   and a server timestamp.
4. The inbound worker validates and dispatches the message to the registered `IOrchestrationAgentAdapter`.
5. Agent output is published through `IAgentOutputSink`.
6. Reply-expected form requests are registered in the pending-reply registry before they are queued to the browser.
7. The outbound worker publishes messages to active SSE subscribers for the matching conversation.
8. A later form response atomically claims the pending reply, validates it against the stored expected form metadata, sends it to the requester, and marks the pending reply completed or cancelled only after successful acceptance.

Accepted browser messages and outbound server messages are appended to per-conversation JSON files under the configured storage folder. New SSE connections replay that conversation history before receiving live events.

The browser stores the current conversation ULID in `localStorage` so a later browser session asks for the same conversation history.

## Pending Replies

A pending reply records:

- Request message ID.
- Conversation ID.
- Return path.
- Expiration time.
- Status.
- Expected form ID.
- Expected form version.
- Validator key.

This prevents a browser from responding to one form while claiming another form ID. Claims are atomic, so concurrent duplicate responses cannot both be accepted. Completed, cancelled, and expired replies are retained for a configurable period to preserve duplicate-response detection, then removed by a cleanup worker.

## Streaming

SSE output is conversation-scoped. Each subscriber has a bounded queue. If a subscriber cannot keep up, the hub disconnects that subscriber instead of allowing unbounded memory growth.

The stream sends:

- `: connected` when opened.
- `: heartbeat ...` comments at the configured interval.
- One SSE event per `YatraMessage`, using the message type as the event name.

The engine implements local JSON replay. The UI waits for SSE before sending text, while received forms remain available over HTTP during reconnects.

## Boundaries

The browser cannot choose internal routing fields. It submits only browser-owned data, and the server constructs the internal envelope. Adapters receive trusted routing metadata through `AgentInput`.

The sample orchestration adapter verifies the resolved return path before processing form responses. A form response that was not routed to `orchestrator/sample` is rejected.

## Current Limitations

- Live subscribers are recreated on reconnect; queue contents and pending replies persist.
- A crash during an external adapter call may repeat that call with the same input ID.
- Conversation messages are replayed from local JSON flat files.
- Authentication and authorization are not implemented.
- Distributed messaging and multi-instance coordination are not implemented.
- The included orchestration project is a sample, not part of the reusable engine.
