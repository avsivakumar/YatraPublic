# Yatra Phase 1 Review

**Author:** Sivakumar Angarai  
**Status:** Historical development record

> This review records the Phase 1 state and requested corrections. It is not a description of the completed Version 1 implementation. See the current documents under `docs/`.

## Review Outcome

Phase 1 has a sound overall structure and is ready for a focused correction pass. Complete the changes in this document before beginning Phase 2.

The review covered the supplied source code, project references, contracts, queues, and tests. The code could not be independently executed in the review environment because the .NET SDK was unavailable. Codex must run the complete build and test suite after making the changes below.

## What Is Already Good

- The solution is separated into `Yatra.Contracts`, `Yatra.AgentIntegration`, and `Yatra.Engine` projects.
- `Yatra.Engine` does not reference a concrete orchestration-agent implementation.
- The agent adapter and output sink are expressed through neutral interfaces.
- Form definitions are declarative and contain no executable UI content.
- Browser messages do not contain a return path.
- A form response refers to the original message using `inReplyTo`.
- Inbound and outbound queues use bounded `Channel<T>` instances.
- The queue full-mode is `Wait`, so messages are not silently discarded.
- Queue operations support cancellation.
- Tests cover basic serialization, queue ordering, bounded-channel waiting, cancellation, and preservation of routing metadata.
- No Phase 2 SSE endpoint, pending-reply registry implementation, orchestration behavior, or React UI was introduced prematurely.

## Required Corrections

### 1. Simplify the Return Path

The current return-path record contains:

```csharp
public sealed record ReturnPath(
    string RequesterType,
    string RequesterId,
    string InvocationId,
    string ReplyDestination);
```

This is more complicated than the intended Yatra model. Every request message already has a unique ULID. That message ULID identifies the individual interaction, so a separate invocation identifier is unnecessary.

Change the return path to a single qualified internal address:

```csharp
public sealed record ReturnPath(string QualifiedPath);
```

Examples:

```text
orchestrator/sample
skills/customer-onboarding
tools/application-search
```

The future pending-reply registry will maintain:

```text
Original request-message ULID -> qualified return path
```

The UI will receive the original message ULID but not the return path. Its response will set `inReplyTo` to that ULID. The engine will resolve the stored path and affix it internally before routing the response.

Update the following as necessary:

- `ReturnPath`
- `PendingReply`
- `RoutableMessage`
- `AgentInput`
- `AgentOutput`
- Existing tests and test fixtures

Do not add a pending-reply registry in this correction pass; that belongs to Phase 2.

### 2. Correct ULID Validation

`YatraUlid.IsValid()` checks length and alphabet membership, but it accepts ULID strings whose first character is greater than `7`. Such strings exceed the 128-bit ULID value range.

Update validation so that:

- The value must contain exactly 26 characters.
- Every character must belong to the Crockford Base32 ULID alphabet.
- Ambiguous characters `I`, `L`, `O`, and `U` are rejected.
- The normalized first character must be between `0` and `7`.
- If lowercase input remains accepted, normalize the first character before checking its range.

Add tests that reject representative values beginning with `8`, `Z`, and their lowercase equivalents.

### 3. Add Message-Envelope Validation

Calling `YatraUlid.IsValid()` in a unit test does not cause invalid messages to be rejected. Add a message-envelope validator in `Yatra.Contracts`, or another Phase 1 location that does not introduce server routing.

The validator must verify:

- `MessageId` is a valid ULID.
- `ConversationId` is a valid ULID.
- `InReplyTo`, when present, is a valid ULID.
- `Type` is registered in `YatraMessageTypes`.
- `Source` is present and not whitespace.
- `Destination` is present and not whitespace.
- `CreatedAtUtc` contains a UTC offset.
- `Payload` is present and is a JSON object for the initial message contracts.
- A response message has an `inReplyTo` value where required.

Keep validation separate from routing and transport. Phase 2 endpoints and workers will call this validator at their boundaries.

The validator should return structured validation results or errors rather than only throwing generic exceptions. Do not introduce a full validation framework unless it is genuinely needed.

### 4. Complete Contract Serialization Tests

The current tests substantially exercise `form.request` and `form.response`. Add serialization/deserialization tests for every initial registered message type:

- `user.text`
- `agent.text`
- `form.request`
- `form.response`
- `status`
- `error`

For each type, verify the JSON property names, enum formatting where applicable, payload preservation, and round-trip behavior.

Also add validator tests covering:

- Missing message ID
- Invalid message ID
- Invalid conversation ID
- Invalid `inReplyTo`
- Unknown message type
- Missing source
- Missing destination
- Invalid or missing payload
- A valid message

### 5. Validate Queue Capacity

Validate `YatraQueueOptions.Capacity` before creating a bounded channel.

- Capacity must be greater than zero.
- Invalid configuration should produce a clear `ArgumentOutOfRangeException` or equivalent configuration error.
- Add tests for zero and negative capacity.

### 6. Expand Queue Tests

Keep the current queue tests and add:

- Outbound single-producer ordering.
- Cancellation of a writer waiting because the queue is full.
- Multiple messages passing through each queue asynchronously.
- Invalid queue capacity.

Do not add hosted queue consumers yet; they belong to Phase 2.

## Design Boundaries to Preserve

While making these corrections:

- Do not implement SSE endpoints.
- Do not implement the pending-reply registry.
- Do not implement the sample orchestration agent.
- Do not implement LLM integration.
- Do not implement a skill loader, skill registry, or skill runtime.
- Do not implement the React UI.
- Do not add agent-provider-specific types to the engine or shared message contracts.
- Do not expose a return path in `YatraMessage` or any future browser-facing JSON.

The first sample will have only the orchestration-agent requester. The architecture must still allow a future skill to request a form directly and receive its reply through the same return-path mechanism.

## Repository and Packaging Cleanup

The submitted ZIP contains `bin` and `obj` directories. These generated files increase the archive from roughly the size of the source tree to many megabytes.

The existing `.gitignore` already lists these directories. Ensure generated artifacts are not committed and exclude them from future review ZIP files. Include only source, tests, documentation, solution/project files, and necessary configuration.

Add a small root `README.md` if one is not already planned for a later pass. For Phase 1 it may contain only:

- Project purpose
- Prerequisites
- Build command
- Test command
- Current phase and limitations

## Required Verification

After implementing the corrections, Codex must run:

```text
dotnet restore
dotnet build
dotnet test
```

All commands must complete successfully with no build errors. Resolve warnings caused by the Phase 1 code where practical.

Codex should also inspect project references and confirm:

- `Yatra.Contracts` references no other Yatra project.
- `Yatra.AgentIntegration` references only `Yatra.Contracts`.
- `Yatra.Engine` does not reference a concrete orchestration implementation.
- No circular project references exist.

## Completion Criteria

The Phase 1 correction pass is complete when:

1. `ReturnPath` contains only `QualifiedPath`.
2. The original request-message ULID remains the future pending-reply lookup key.
3. ULID overflow values beginning above `7` are rejected.
4. Message-envelope validation exists and is tested.
5. Every initial message type has serialization coverage.
6. Invalid queue capacities are rejected and tested.
7. Additional queue ordering and cancellation tests pass.
8. All projects build successfully.
9. All tests pass.
10. No Phase 2 implementation has been introduced.
11. Generated `bin`, `obj`, test-result, and coverage files are excluded from the repository and the next review archive.

When finished, provide a brief change summary and the output of the build and test commands. Then prepare a clean Phase 1 ZIP for review before beginning Phase 2.
