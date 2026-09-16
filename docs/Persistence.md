# Persistence and Restart Recovery

The engine persists operational state as JSON in the same storage root as conversation history. Configure `Yatra:StorageFolder` to choose the folder. The default is `library` under the app executable directory, not the shell's current directory. Keep this folder when updating or restarting the app.

| Location | Contents |
| --- | --- |
| `engine-state.json` | Accepted inbound messages and completion checkpoints; outbound messages and delivery checkpoints; pending form return paths, expected form/version/validator metadata, expiry, and terminal status |
| `conversations/{conversationId}.messages.json` | Browser-facing conversation history for SSE replay |
| `engine-state.lock` | Exclusive process ownership; the operating system releases the lock on exit or crash |
| Browser local storage, `yatra:*` | Form values/errors/status, response retry identities, response correlation, outcomes, composer draft, and a conversation cache |

## Commit and Recovery Rules

1. HTTP acceptance commits the inbound message before returning an acknowledgement. History is a separate projection; the worker repairs it before handling recovered input.
2. An accepted ID and its original payload stay in the inbox even after completion, so retry deduplication survives restart.
3. Worker input remains unfinished until processing is committed. Outputs from an adapter invocation are staged, then committed together with input completion and the pending form's terminal status. New form routing metadata is committed with its outbound message.
4. The outbound worker saves conversation history before publishing to SSE. It marks delivery complete afterward. A crash before that checkpoint may replay the same message ID; the UI deduplicates it. An offline browser recovers from history.
5. Startup restores unfinished queues, return paths, expiry times, and terminal records. Interrupted processing claims return to pending. Expiry times are not extended by restart. Completed, cancelled, and expired forms stay non-actionable.
6. Snapshot replacement uses a temporary file, disk flush, and atomic rename. A failed update does not change the live snapshot. Corrupt, incomplete, or unsupported snapshots fail startup rather than being overwritten with empty state.

Only one engine process may own a storage folder. Live HTTP/SSE connections, synchronization primitives, worker leases, and reconnect timers are recreated; their messages and meaningful processing state are persisted. Persistent disk queues replace the old bounded memory queues; the old inbound/outbound capacity options no longer limit queue length. SSE subscriber buffers remain bounded. Terminal records and completed IDs are retained for deduplication, so monitor disk growth and back up the storage folder with the engine stopped.

## Adapter Boundary

Restart recovery provides at-least-once adapter execution for unfinished input. A process can crash after an external operation succeeds but before its local completion checkpoint is committed. An adapter performing external side effects must deduplicate on `AgentInput.Message.MessageId` at that external boundary. JSON persistence cannot make an external HTTP call and a local file transaction atomic.

Adapter outputs are staged until `HandleAsync` returns successfully. This matches the sample's complete-message behavior; token streaming is not implemented. An explicit retryable application failure is committed as an error and waits for a new user attempt; shutdown interruption preserves unfinished input for recovery.

## Browser Recovery

Reloading restores form drafts and locked acknowledged forms. A request interrupted before acknowledgement becomes retryable, retaining the same response ULID and payload. Server history also replays `form.response` events internally to reconstruct correlation and form outcomes. Storage quota or browser restrictions can prevent draft saving; the form displays a warning, and response submission requires successfully saving its retry identity first.

Browser storage is local to the browser profile and origin. Clearing it removes unsent drafts; changing the dev-server origin creates a separate browser store. It does not erase server history or pending reply records.

## Existing Data

Existing conversation JSON files are preserved. Routing metadata and queued work lost before this persistence change cannot be recovered from those older history files alone. New work and pending forms created by this version are restart-recoverable. No return paths are guessed from browser-supplied values or historical display messages.

## Verification

Tests cover unfinished inbox/outbox recovery, atomic processing completion, interrupted claim recovery, completed/cancelled/expired state, immutable retry identities, failed commits, corrupt files, exclusive ownership, and a form submitted successfully across separate app-host lifetimes. Browser tests cover draft/locked-state restoration and retry identity preservation across remounts.

Verified on 2026-09-16: the Smart 8 package reports 210 backend tests; 51 frontend tests passed in the review workspace, and the production build succeeded. The earlier live browser check restored an entered draft after reload, submitted it after an engine restart, and replayed its agent confirmation after another reload. Acknowledged forms persist as submitted until a correlated business outcome arrives.
