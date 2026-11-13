# Existing projects

When working in an existing codebase:

1. **Learn before changing.** Read the README, `AGENTS.md` project context, build files and the code around the
   change. Identify the frameworks, versions, patterns and conventions actually in use.
2. **Preserve established choices.** Keep the existing frameworks, libraries, architecture and style, even when
   you would choose differently for a new project. Migration requires explicit approval.
3. **Describe accurately.** When explaining or documenting, distinguish the *current implementation* from
   *recommended standards* and *proposed changes*.
4. **Change incrementally.** Keep the system building and tests passing at each step. Avoid drive-by refactoring
   outside the task.
5. **Respect compatibility.** Public APIs, data formats, configuration and persisted data may have consumers you
   cannot see; flag breaking changes and get approval.
6. **Improve safely.** When you see problems outside the task, report them as suggestions rather than fixing them
   unasked.
7. **Record knowledge.** Add discovered constraints, gotchas and decisions to the project context section of
   `AGENTS.md` when the user agrees.
