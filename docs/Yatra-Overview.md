# Yatra Overview

**Author:** Sivakumar Angarai  
**Version:** 1

Yatra is a framework-neutral user-interface shell through which AI agents communicate with people using conversation and structured interactions.

The central idea is:

> The agent describes the interaction; Yatra presents it to the human and returns the response.

An agent can send ordinary text or a declarative form. Yatra renders the request inside the conversation, collects the response, and returns it through the engine to the originating requester. This avoids building a separate screen for every question, correction, choice, confirmation, or approval.

## Architecture at a glance

Yatra has three principal parts:

1. The React browser UI displays conversation and dynamic forms.
2. The ASP.NET Core engine transports, persists, correlates, and routes messages.
3. An agent adapter connects Yatra to an orchestration agent or another agent implementation.

Browser-to-engine messages use HTTP. Engine-to-browser messages use Server-Sent Events (SSE). Every message has a ULID, and form responses identify the original form message. This supports correlation, deduplication, replay, and safe retry.

## Dynamic forms

An agent describes a form using versioned metadata: identity, title, fields, initial values, constraints, and submit or cancel labels. Version 1 supports text, textarea, number, date, datetime, select, multiselect, checkbox, display-only, and hidden fields.

The browser validates the definition, renders only trusted built-in controls, preserves entered values, and returns typed values. Server and application validation remain authoritative.

## Human-in-the-loop

Yatra enables human-in-the-loop interaction without owning the human-in-the-loop process. An agent runtime may decide to pause for approval, clarification, correction, or missing information. Yatra surfaces that request and returns the human response. The agent or application owns authority, workflow state, business meaning, and continuation.

## Reliability

Version 1 demonstrates:

- Draft preservation after failures and reloads
- Stable response identity for transport retries
- Duplicate-submission prevention
- Durable HTTP acceptance distinct from business completion
- Bounded SSE reconnection and cursor-based replay
- ULID-based replay deduplication
- Clear failure for malformed or unsupported forms
- Persistent queues, pending replies, and conversation history

## Deliberate boundary

Yatra is not an AI model, agent framework, workflow engine, authorization system, approval-policy engine, or microfrontend host. It is the reusable interaction layer between agents and people.

Version 1 is a working architectural sample and foundation. Its value is the clean separation it establishes: agents determine what interaction is needed; Yatra makes that interaction usable by a person.
