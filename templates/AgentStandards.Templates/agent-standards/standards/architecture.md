# Software architecture

## Principles

- **Respect the existing architecture.** Identify the current structure (layers, modules, boundaries, patterns)
  before changing anything, and extend it consistently. A consistent "good enough" architecture beats a mix of
  ideal ones.
- **Separate concerns.** Keep domain logic independent of UI, transport, persistence and infrastructure where the
  project already does so. Do not introduce extra layers into a project that deliberately does not use them.
- **Explicit dependencies.** Pass dependencies in (constructor/parameters or the project's DI mechanism) rather
  than reaching for globals or service locators.
- **High cohesion, low coupling.** Group code by feature or responsibility; depend on stable abstractions only
  where variation is real. Avoid speculative interfaces and generic frameworks.
- **Simple first.** Choose the simplest design that meets current, known requirements. Record the reasons for
  any added complexity.

## Making changes

1. Describe the current implementation accurately before proposing changes.
2. For a significant change (new layer, framework, pattern, data store, cross-cutting restructure), write a short
   proposal: problem, options considered, recommendation, migration path, risks. Get approval before
   implementing.
3. Prefer incremental migration that keeps the system working at every step over big-bang rewrites.
4. Record significant decisions as architecture decision records (ADRs) or in the project's existing format.

## Boundaries

- Validate and translate data at system boundaries (APIs, UI input, files, messages, external services).
- Keep transport and persistence models separate from domain models when the project already distinguishes them.
- Make side effects (I/O, time, randomness) easy to substitute in tests.

## Anti-patterns to avoid

God classes or modules, circular dependencies, leaking infrastructure types into the domain, copy-paste
reuse, hidden global state, and abstractions with a single implementation "just in case".
