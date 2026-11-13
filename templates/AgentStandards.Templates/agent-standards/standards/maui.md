# .NET MAUI applications

Extends the [.NET standards](dotnet.md).

## Architecture

- Follow the project's established presentation pattern (commonly MVVM) and its existing MVVM, navigation,
  dependency-injection and UI component libraries. Do not introduce or swap frameworks without approval.
- Keep views thin: UI logic in view models or equivalent, business logic in services, platform code isolated.
- Register services, view models and pages in `MauiProgram` using the built-in dependency injection unless the
  project uses another container.

## XAML and UI

- Use compiled bindings (`x:DataType`) for performance and build-time checking.
- Prefer `Grid` and simple layouts over deeply nested `StackLayout`s; use `CollectionView` (not `ListView`) for
  lists, with item templates kept light.
- Put shared colours, styles and dimensions in resource dictionaries; support light and dark themes when the app
  does.
- Provide `SemanticProperties` (description, hint, heading level) for accessibility.

## Threading and lifecycle

- Never block the UI thread. Marshal UI updates with `MainThread` / the dispatcher.
- Handle app lifecycle events (sleep/resume) and persist state where needed.

## Platform-specific code

- Prefer cross-platform APIs. Put platform code in `Platforms/<platform>/` or partial classes and conditional
  compilation, behind an interface the shared code uses.
- Declare required permissions and request them at runtime with clear user-facing reasons.
- Changes to entitlements, signing, provisioning, manifests or store metadata need approval.

## Performance

- Optimise images (correct sizes via `MauiImage` and `BaseSize`), avoid unnecessary layout passes, and defer
  non-critical startup work.
- Measure on Release builds and real devices before optimising.

## Testing

Unit-test view models and services without the UI framework. Keep UI automation for critical journeys.
