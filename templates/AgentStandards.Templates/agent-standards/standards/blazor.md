# Blazor applications

Extends the [.NET standards](dotnet.md).

- Respect the project's render modes (static server rendering, interactive server, WebAssembly, auto) and
  hosting model; changing them is an architecture change.
- Keep components small and focused; move non-UI logic into injected services.
- Use parameters for input and `EventCallback` for output; do not mutate parameters inside a component.
- Avoid unnecessary re-rendering: use `@key` for list items, override `ShouldRender` only with measurement, and
  virtualise long lists with `Virtualize`.
- Dispose subscriptions and timers (`IDisposable`/`IAsyncDisposable`).
- Use `[SupplyParameterFromForm]`, `EditForm` and data-annotation validation (or the project's validator).
- Keep JavaScript interop minimal, isolated in modules, and asynchronous.
- Never trust the client: in WebAssembly, all code and data shipped to the browser is visible; enforce
  authorisation on the server API.
- Use semantic HTML and the [accessibility](accessibility.md) standard for all components.
