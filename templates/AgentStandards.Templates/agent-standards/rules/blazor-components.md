---
description: Conventions for Blazor components.
globs: ["**/*.razor", "**/*.razor.cs", "**/*.razor.css"]
---
# Blazor components

- Keep components small; move non-UI logic into injected services.
- Do not mutate `[Parameter]` properties; raise changes through `EventCallback`.
- Use `@key` for repeated elements and `Virtualize` for long lists.
- Dispose subscriptions and timers; keep JavaScript interop in isolated modules.
- Use semantic HTML with accessible names for interactive elements.
