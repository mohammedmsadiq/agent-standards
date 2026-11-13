# Reusable .NET libraries

Extends the [.NET standards](dotnet.md).

## Public API

- Keep the public surface small and intentional; make types `internal` or `sealed` unless extension is a
  designed feature.
- Follow the .NET Framework Design Guidelines for naming, exceptions and overloads.
- Document every public member with XML documentation comments and enable documentation file generation.
- Treat any change to public signatures, behaviour or thrown exceptions as a potential breaking change; follow
  semantic versioning and get approval for breaking changes.
- Consider package validation (`EnablePackageValidation`) and API baseline tooling to catch accidental breaks.

## Compatibility

- Choose target frameworks deliberately (for example the oldest supported framework consumers need plus modern
  targets for performance). Changing them needs approval.
- Avoid unnecessary dependencies; every dependency becomes your consumers' dependency.
- Do not depend on application frameworks or hosting models unless that is the library's purpose.

## Behaviour

- Accept `CancellationToken` on asynchronous APIs; use `ConfigureAwait(false)` where the project does.
- Do not write to the console or global state; accept `ILogger` or `ILoggerFactory` when logging is needed.
- Be thread-safe where documented and say so in documentation.

## Packaging

Deterministic builds, SourceLink, symbols, a README and licence metadata in the package; release notes for every
version.
