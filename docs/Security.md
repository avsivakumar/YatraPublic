# Security

Yatra Version 1 is an architectural sample. It demonstrates secure boundaries, but it does not provide production identity, authorization, tenancy, or secret management.

## Credentials and secrets

- Never commit API keys, tokens, passwords, or connection strings.
- Never place secrets in the React application or any `VITE_*` variable; browser bundles are public to their users.
- A source-code constant is not secret and is recoverable from compiled binaries.
- Keep the Fake provider as the distributable default. Use a local secret store or environment-backed configuration in a production integration.
- Revoke any credential that entered a shared archive or Git history; deleting it in a later commit is insufficient.

Before publication, scan the working tree and history for credential patterns and inspect packaged binaries and archives.

## Trust boundaries

The browser supplies only browser-owned message data. The server creates trusted timestamps and internal source/destination routing. Return paths are stored server-side and resolved from `inReplyTo`; they are never accepted from or disclosed to the browser.

Conversation ULIDs are correlation identifiers, not authorization credentials. A production deployment must authenticate users and authorize access to every conversation and form response.

## Dynamic forms

Yatra renders only known field types through built-in React components. Form metadata is data, not executable HTML or JavaScript. Unknown or malformed definitions fail closed.

Client validation improves usability. The engine and adapter must still validate field identity, type, required values, constraints, options, form version, and interaction status.

Hidden fields are visible to and modifiable by a browser user. Never use them as proof of identity, authority, price, entitlement, or any other security-sensitive fact.

## Transport and browser concerns

- Use HTTPS outside local development.
- Configure `Yatra:AllowedCorsOrigins` with exact trusted origins; do not use a permissive production policy.
- Apply authentication and authorization to both HTTP message submission and SSE subscriptions.
- Treat local storage as user-controlled and origin-scoped, not as a trusted database.
- Add appropriate CSP, security headers, rate limits, request-size limits, and abuse controls in a production host.

## Persistence and logs

The sample stores messages and form values as local JSON. Protect the storage folder with operating-system permissions, encryption and retention appropriate to the data. Do not log API keys, authorization headers, full sensitive payloads, or model content by default.

The single-process file store is not a tenant-isolation boundary. Production systems should use a persistence implementation designed for their security and availability requirements.

## External side effects

Restart recovery is at-least-once at the adapter boundary. External operations must use the input message ULID as an idempotency key or otherwise prevent repeated side effects.

## Release checklist

- [ ] Fake provider is the default
- [ ] No real secrets exist in source, configuration, archives, binaries, or Git history
- [ ] Documentation uses placeholders only
- [ ] Dependency and secret scans are clean
- [ ] CORS is explicit
- [ ] Storage and sample data contain no private information
- [ ] Authentication and authorization limitations are prominent
- [ ] `dotnet test`, `npm test`, and `npm run build` pass

Report suspected vulnerabilities privately to the project maintainer rather than opening an issue containing exploit details or private data.
