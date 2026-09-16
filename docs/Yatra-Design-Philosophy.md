# Yatra Design Philosophy

**Author:** Sivakumar Angarai  
**Version:** 1

## Human interaction is a distinct concern

Reasoning, tool use, and workflow execution do not automatically provide a usable interface for a person. Yatra gives agents a consistent way to converse and request structured information without absorbing the responsibilities of the agent runtime or business application.

## Describe the interaction, not the screen

The sender describes required information using field types, labels, choices, initial values, and constraints. Yatra chooses trusted controls for the client. The agent never sends React code, HTML, or executable UI behavior.

## Conversation and structure belong together

Conversation supplies context and flexibility; forms supply precision and predictable data. Yatra places forms inline so users retain the context in which an interaction arose.

## Framework neutrality

Yatra is independent of a model vendor, agent SDK, and orchestration library. The adapter boundary translates between Yatra messages and the selected agent implementation.

## Narrow responsibility

Yatra renders interactions, captures responses, transports and correlates messages, preserves drafts and retry identity, and presents validation and recovery states. It does not decide when approval is required, who is authorized, how workflow state changes, or what business consequence follows.

## Metadata before custom UI

Version 1 favors metadata-driven forms. Remotely supplied components or microfrontends would introduce deployment, compatibility, isolation, styling, and security concerns. They should be considered only if real applications demonstrate that declarative forms are insufficient.

## Identity is part of the protocol

Every message has a ULID, and each response identifies its request. Identity enables correlation, safe retries, deduplication, replay, and recovery; it is not merely diagnostic metadata.

## Acceptance is not completion

- `submitting` means the HTTP request is in progress.
- `submitted` means the engine durably accepted it.
- `completed` or `cancelled` means a correlated agent outcome arrived.

This prevents transport success from being confused with business success.

## Fail clearly and safely

Malformed and unsupported forms do not render partially. Client validation improves usability but never replaces authoritative server and application validation.

## Evolve through contracts

Extensions should preserve existing meanings, prefer optional backward-compatible properties, version breaking changes, fail safely, and maintain separation between UI transport and business semantics.

The enduring principle is:

> Yatra makes human participation easy without taking ownership of the process in which that participation occurs.
