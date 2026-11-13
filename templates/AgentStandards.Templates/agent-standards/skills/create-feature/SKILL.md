---
name: create-feature
description: Implement a new feature end to end in the style of the existing project, with tests and documentation. Use when asked to add a feature, endpoint, screen or capability.
---
# Create a feature

1. **Clarify requirements:** behaviour, inputs/outputs, edge cases, non-functional needs. Ask about ambiguities
   that change the design.
2. **Study a similar existing feature** and follow its structure, naming, patterns, error handling and tests.
3. **Plan briefly:** files to add or change, data or contract changes, and risks. Ask for approval if the feature
   needs new dependencies, architecture changes, schema migrations or security-sensitive changes.
4. **Implement incrementally,** building as you go. Validate input at boundaries and handle errors explicitly.
5. **Add tests** for the main path, edge cases and failures. Run them along with the existing suite.
6. **Update documentation** (README, API docs, changelog) where behaviour is user-visible.
7. **Report** what was built, how it was verified, and anything not done or needing follow-up.
