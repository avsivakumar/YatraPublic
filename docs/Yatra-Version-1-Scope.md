# Yatra Version 1 Scope

## Objective

Version 1 demonstrates a complete and reliable path between a browser user and one orchestration adapter using conversation and metadata-driven dynamic forms. It is an open-source architectural sample, not a complete production agent platform.

## Included

- React conversation UI with inline declarative forms
- Ten trusted field types, initial values, constraints, help text, and submit/cancel actions
- HTTP browser-to-engine transport and SSE engine-to-browser delivery
- ULID identity, request-response correlation, deduplication, heartbeats, reconnection, and replay
- Draft retention, idempotent retry, duplicate-submit protection, and acceptance/outcome separation
- Persistent inbox, outbox, pending replies, and conversation history
- Restart recovery in a single engine process
- Framework-neutral adapter contract and one sample orchestration adapter
- Fake LLM client and optional OpenAI client for private testing
- Complete-message responses; token-by-token LLM streaming is not implemented
- Local clearing of the displayed conversation without deleting server history

## Deliberate exclusions

- Multi-agent orchestration or workflow management
- Approval policy, authentication, authorization, roles, or tenant isolation
- Distributed or multi-instance coordination
- Production database persistence
- Application-specific microfrontends or remotely supplied executable UI
- File upload, signature, rich-text editing, editable tables, or visual form design
- Agent administration, monitoring, or hosted deployment services

## Operational limits

- Only one process may own a storage folder.
- External side effects must deduplicate by input message ID.
- Browser drafts and display preferences are local to the browser profile and origin.
- Clearing the UI does not delete authoritative history.
- Hidden values must not be trusted for identity or authorization.
- Browser validation is for usability; server and application validation are authoritative.
- Local datetime values carry no timezone conversion.
- Source and binaries must never be distributed with credentials.

## Compatibility direction

Future versions should preserve existing meanings, prefer optional additions, version breaking changes explicitly, fail safely for unsupported definitions, and remain independent of particular agent and model providers.

Possible future work includes additional field types, alternative persistence providers, authentication examples, more adapters, internationalization, and distributed-engine support. These are possibilities, not Version 1 commitments.
