# Changelog

All notable changes to Yatra are recorded here. The project is currently preparing its first public sample release.

## [Unreleased] - Version 0.1 / Version 1 sample

### Added

- React conversation UI and ASP.NET Core engine
- HTTP browser-to-engine submission and SSE engine-to-browser delivery
- ULID message and conversation identity
- Asynchronous inbound and outbound queues
- Framework-neutral orchestration adapter boundary
- Dynamic forms with text, textarea, number, date, datetime, select, multiselect, checkbox, display, and hidden fields
- Client and authoritative server validation
- Pending-reply correlation with server-side return paths
- JSON persistence for queues, pending replies, and conversation replay
- Browser draft, retry-identity, outcome, composer, and view-state persistence
- Fake LLM and optional OpenAI client
- Local Clear all messages feature
- Architecture, protocol, adapter, developer, persistence, security, testing, user, scope, philosophy, overview, and use-case documentation

### Reliability hardening

- Fail-closed validation for unsupported and malformed form definitions
- Draft preservation and safe resubmission after transport failure
- Immediate UI submission guard and serialized server acceptance
- Idempotent retries using the original response ULID and payload
- Conflict response when an existing ULID is reused with different content
- `submitted` state separated from correlated `completed` and `cancelled` outcomes
- Early SSE outcomes protected from late HTTP acknowledgement races
- Bounded SSE reconnection, single active stream, cursored replay, and ULID deduplication
- Exhaustive server validation for all Version 1 field types and constraints
- Atomic engine snapshot, interrupted-claim recovery, and restart tests

### Known limits

- Single process per storage folder
- At-least-once adapter invocation across crash boundaries
- No authentication, authorization, tenancy, distributed coordination, or production database provider
- Complete-message agent responses; token-by-token LLM streaming is deferred

## Development phases

- **Phase 1:** established the browser, engine, basic messaging, and initial review baseline.
- **Phase 2:** developed asynchronous browser-to-engine and engine-to-browser communication.
- **Phase 3:** introduced message identity, correlation, routing, and adapter boundaries.
- **Phase 4:** completed dynamic-form interaction and orchestration integration.
- **Phase 5:** completed the end-to-end architectural sample, followed by Smart 7 and Smart 8 reliability, validation, lifecycle, persistence, and UI hardening.

The phase summary is intentionally architectural; Git history should be used for file-level change details after publication.
