# New projects

When asked to create a new project:

1. **Clarify the essentials:** purpose, users, platforms, constraints, expected scale, and the team's existing
   technology preferences. Recommend options with trade-offs rather than deciding silently.
2. **Start from official templates** for the chosen stack, at a current, supported version (prefer long-term
   support releases for long-lived systems).
3. **Use built-in capabilities first;** add third-party dependencies only with justification.
4. **Set up the basics from day one:**
   - README with build/test/run instructions;
   - formatter and linter configuration (for example `.editorconfig`);
   - a test project or test folder with at least one meaningful test;
   - `.gitignore` appropriate to the stack;
   - CI that builds and tests on every change;
   - dependency version locking and vulnerability auditing;
   - an `AGENTS.md` with project context (run `agent-standards init`).
5. **Keep the initial architecture simple** and proportional to known requirements. Document the decision and
   the conditions under which it should evolve.
6. **Configuration and secrets:** environment-based configuration from the start; no secrets in source.
7. **Licensing:** confirm the licence with the owner before adding licence files or publishing.
