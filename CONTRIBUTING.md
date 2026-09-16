# Contributing to Yatra

Thank you for helping improve Yatra. Contributions should preserve its narrow purpose as a framework-neutral agent-to-human interaction UI and engine sample.

Yatra was originally conceived, designed, and created by **Sivakumar Angarai**. Contributor copyright is recognized under the repository's MIT License.

## Before starting

Open an issue for substantial protocol changes, new field types, persistence replacements, or architectural dependencies. Small fixes and documentation improvements may proceed directly.

## Development setup

Install .NET SDK 10, Node.js 24 or later, and npm. Follow the root [README](README.md) for startup instructions.

Run all checks before submitting:

```powershell
dotnet build .\Yatra.slnx
dotnet test .\Yatra.slnx
cd .\yatra-ui
npm ci
npm test
npm run build
```

See [Testing](docs/Testing.md) for coverage expectations.

## Design rules

- Keep `Yatra.Engine` independent of concrete orchestration implementations.
- Preserve the browser/server trust boundary; clients cannot choose internal routing.
- Treat the [Message Protocol](docs/Message-Protocol.md) as canonical.
- Prefer optional, backward-compatible contract additions.
- Version breaking changes explicitly and document migration behavior.
- Render only trusted built-in controls; do not accept executable UI from messages.
- Keep transport acceptance separate from agent or business completion.
- Preserve ULID identity, correlation, retry, and deduplication guarantees.
- Keep business workflow, approval authority, and domain rules outside Yatra.

## Pull requests

A pull request should:

- Explain the problem and the chosen boundary
- Include focused tests for new and changed behavior
- Update all affected documentation and examples
- Avoid unrelated formatting or refactoring
- State compatibility and persistence effects
- Add a changelog entry when user-visible behavior changes
- Pass server tests, frontend tests, TypeScript checking, and production build

## Credentials and private data

Never commit real keys, tokens, connection strings, private prompts, customer data, or credential-bearing binaries. Use the Fake provider for committed examples and placeholders in documentation. Follow [Security](docs/Security.md).

## Commit and code quality

Use clear, scoped commits. Follow existing C# nullable, cancellation, asynchronous, and dependency-injection patterns and existing React/TypeScript conventions. Do not swallow cancellation or convert caller cancellation into an application error.

## Reporting issues

Include reproduction steps, expected and actual behavior, relevant logs with secrets and sensitive values removed, and environment versions. Report security vulnerabilities privately rather than in a public issue.

By contributing, you agree that your contribution is licensed under the repository's MIT License.
