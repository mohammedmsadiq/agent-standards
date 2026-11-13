# Coding conventions

## Follow the project first

The repository's existing style, formatter and linter configuration (for example `.editorconfig`, Prettier,
ESLint, Ruff, `dotnet format`, Checkstyle) are authoritative. Match the surrounding code when no configuration
exists. Never reformat files you are not otherwise changing.

## Readability

- Use descriptive, intention-revealing names. Avoid abbreviations that are not standard in the domain.
- Keep functions short and focused on one responsibility; extract helpers when a block needs a comment to
  explain *what* it does.
- Prefer early returns over deep nesting.
- Write comments that explain *why*, not *what*. Remove commented-out code.
- Avoid magic numbers and strings; use named constants or configuration.

## Correctness

- Prefer immutability and pure functions where practical.
- Make invalid states unrepresentable with types where the language allows it.
- Handle null/undefined/optional values explicitly; enable the language's strictness features where the project
  already uses them (for example nullable reference types, TypeScript `strict`, type hints with a checker).
- Do not ignore return values or errors.

## Consistency

- One concept, one name, across code, tests, documentation and UI text.
- Keep public APIs small; expose only what consumers need.
- Keep generated code out of manual edits; change the generator or its inputs instead.
