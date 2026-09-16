# Troubleshooting

## The UI does not connect

- Confirm the AppHost is running and listening on the expected URL.
- Confirm Vite is running on `http://localhost:5173` or set `VITE_YATRA_API_BASE_URL`.
- If origins differ, add the exact UI origin to `Yatra:AllowedCorsOrigins`.
- Inspect the browser network panel for the conversation SSE request.

## Messages cannot be sent

New text messages require an active SSE connection. Wait for `connected` or select **Retry connection** after automatic attempts are exhausted. A form already on screen may still submit over HTTP while SSE reconnects.

## A form is unsupported

Yatra fails closed when a field type, constraint, initial value, or form structure is invalid. Compare the request with [Message Protocol](Message-Protocol.md) and inspect server logs without exposing field values.

## A form remains submitted

`submitted` means the response was durably accepted, not that agent processing completed. The form changes only after a correlated completion, cancellation, or error arrives. Check adapter processing, outbound state, and SSE connectivity.

## A retry receives 409 Conflict

A message ULID was reused with different content. A transport retry must reuse the original ULID and identical payload. An edited response or a new attempt after explicit rejection must use a new ULID and identify the replaced response.

## Drafts are missing

Drafts use browser local storage and are scoped to browser profile and origin. Private browsing, storage restrictions, cleared site data, or a changed port/origin can remove or isolate them.

## Restart recovery fails

- Confirm the configured storage folder still exists and is writable.
- Ensure only one engine process owns it.
- Do not replace a corrupt snapshot with empty data; preserve it for diagnosis.
- Review [Persistence](Persistence.md) for recovery guarantees and limits.

## OpenAI requests fail

Use the Fake provider to isolate Yatra from external-model issues. For private OpenAI testing, verify the provider, model, key, network access, and timeout. Never paste the real key into an issue, log excerpt, or shared archive.

## Build or test failures

Record the .NET SDK, Node.js, npm, browser, and operating-system versions. Run the exact commands in [Testing](Testing.md), beginning with `npm ci` for a clean UI dependency installation.
