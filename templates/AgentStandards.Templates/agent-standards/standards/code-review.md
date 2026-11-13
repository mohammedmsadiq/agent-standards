# Code review

## What to check, in order

1. **Correctness:** does the change do what it claims? Edge cases, error handling, concurrency, data integrity.
2. **Security:** input validation, authorisation, secrets, injection, unsafe deserialisation, path handling.
3. **Design fit:** consistent with the existing architecture and conventions; no unnecessary abstraction.
4. **Tests:** meaningful tests for new behaviour and bug fixes; tests pass.
5. **Performance and resource use** on hot paths.
6. **Readability and maintainability:** naming, structure, comments that explain why.
7. **Documentation and release notes** updated where behaviour changed.

## How to report

- Lead with the most severe issues. For each: location, the problem, a concrete failure scenario, and a
  suggested fix.
- Distinguish **blocking** issues from **suggestions** and **questions**.
- Be specific and verifiable; do not report speculative issues as facts.
- Acknowledge good decisions briefly when useful.

Review agents and skills are read-only: they report findings and do not change code unless asked.
