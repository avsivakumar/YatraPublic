# Dynamic Form Reliability Review

Reviewed and implemented against the supplied Yatra Dynamic Form Reliability Design on 2026-09-16.

## Existing behavior verified

- Existing envelopes, ULIDs, return-path routing, pending-reply claims, sample adapters, and text messaging remain intact.
- Original text, textarea, number, date, select, and checkbox controls remain supported.
- Draft values already survived ordinary HTTP failures; conversation messages already used ULID deduplication.
- File-backed conversation history and application-versus-transport SSE error distinction already existed.

## Completed gaps

- Added datetime, multiselect, display, hidden, initial values, disabled/read-only state, and help text to UI and .NET contracts.
- Added fail-closed definition checks, integer/selection/date constraints, strict value conversion checks, accessible field errors, and first-invalid-field focus.
- Unified keyboard/button submission with an immediate in-flight guard. HTTP acknowledgement changes the form to submitted; only correlated agent confirmation completes or cancels it. Early SSE outcomes take precedence over late HTTP acknowledgements.
- Added exhaustive server validation for all ten field types, integer and date bounds, multiselect types/options/uniqueness/counts, permitted hidden JSON values, and rejection of display values and unsupported types.
- Preserved response IDs and payloads for transport retries; new attempts after explicit rejection or edits reference the replaced response.
- Added serialized HTTP acceptance, idempotent replay acknowledgements, and conflicts for changed content under an existing ID.
- Added bounded SSE reconnect, single active stream, resume cursor support, replay suppression before form outcomes, and manual retry after exhaustion.
- Kept received forms available for HTTP submission during SSE recovery.
- Made asynchronous adapter validation rejection retryable after releasing its pending-reply claim.
- Updated protocol, adapter, architecture, developer, and user guides.

## Verification

- 210 .NET tests pass across contracts, engine, orchestration, and application host, including direct validation tests for every field type and constraint.
- 51 UI/transport/validation tests pass, including early SSE outcomes versus late HTTP acknowledgements and published request examples; the published response example is tested against the HTTP endpoint.
- TypeScript and Vite production build pass.
- Live browser smoke test with the Fake LLM provider: form request, required-field errors, focus transfer, successful acknowledgement, locked controls, and correlated agent confirmation.
- Desktop and mobile form layouts inspected in the browser.

## Scope and limits

The follow-up persistence implementation closes the original restart and draft-restoration gaps: JSON inbox/outbox checkpoints and pending replies restore on startup, and browser local storage restores drafts and retry identities. See [Persistence](Persistence.md). The engine remains single-process, and interrupted external adapter side effects require deduplication by input message ID. Adapter/application validation remains authoritative. Datetime values are local ISO date-times, and actions remain submit/cancel. Verification counts include persistence and the server-validation and lifecycle corrections.

The definitive contract and defaults are in [Message Protocol](Message-Protocol.md).
