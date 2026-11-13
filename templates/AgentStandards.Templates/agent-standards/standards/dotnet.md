# .NET applications

Applies to C#, F# and Visual Basic projects. The repository's own conventions take precedence.

## Platform and build

- Respect the target frameworks, SDK version (`global.json`) and language version already in use. Changing them
  is a migration and needs approval.
- Prefer supported, long-term-support .NET releases for new work.
- Use central package management (`Directory.Packages.props`) and shared `Directory.Build.props` when the
  solution already does; do not introduce them as a drive-by change.
- Treat warnings seriously; do not suppress analyzers without a justification comment.
- Keep `<Nullable>enable</Nullable>` semantics: do not silence nullable warnings with `!` unless provably safe.

## C# conventions

- Follow `.editorconfig` and `dotnet format`. Default to the .NET naming guidelines: PascalCase for types and
  members, camelCase for locals and parameters, `_camelCase` for private fields, `I` prefix for interfaces,
  `Async` suffix for asynchronous methods.
- Use file-scoped namespaces, `var` and modern language features consistently with the surrounding code.
- Prefer records for immutable data, `readonly` where possible, and pattern matching for clarity.

## Asynchrony and resources

- Use `async`/`await` end to end for I/O; never block with `.Result`, `.Wait()` or `GetAwaiter().GetResult()`
  in application code.
- Accept and pass `CancellationToken` through asynchronous call chains.
- Dispose `IDisposable`/`IAsyncDisposable` resources with `using`.
- Use `IHttpClientFactory` (or a long-lived `HttpClient`) rather than creating clients per request.

## Built-in first

Prefer `Microsoft.Extensions.*` (dependency injection, configuration, options, logging, hosting, caching,
resilience), `System.Text.Json`, and the base class library before third-party packages.

## Errors and logging

- Throw specific exception types; use guard helpers such as `ArgumentNullException.ThrowIfNull`.
- Use `ILogger<T>` with structured message templates (`logger.LogInformation("Order {OrderId} placed", id)`),
  and consider `LoggerMessage` source generation on hot paths.

## Testing

Use the test framework already in the solution (xUnit, NUnit or MSTest) and its existing assertion and mocking
libraries. Run `dotnet test` before reporting completion.
