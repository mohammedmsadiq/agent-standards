---
name: review-blazor-component
description: Review Blazor components for render-mode correctness, parameters, state, rendering performance, disposal, JavaScript interop, security and accessibility. Use when creating or reviewing Blazor UI.
---
# Review a Blazor component

1. **Render mode and hosting:** the component works in the project's render mode; no server-only APIs in
   WebAssembly code paths.
2. **Parameters and state:** parameters are not mutated internally; changes flow out via `EventCallback`; state
   ownership is clear.
3. **Rendering:** `@key` on repeated elements, `Virtualize` for long lists, no expensive work in render logic.
4. **Lifecycle:** asynchronous initialisation handled correctly; subscriptions and timers disposed.
5. **JavaScript interop:** minimal, isolated in modules, asynchronous, and disposed.
6. **Security:** no secrets or authorisation decisions on the client; server APIs enforce access.
7. **Accessibility:** semantic HTML, labels, focus management, keyboard support.
8. **Report** findings by severity with concrete fixes.
