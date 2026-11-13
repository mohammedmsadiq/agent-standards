---
name: review-code
description: Review code changes for correctness, security, design fit, tests and maintainability, and report prioritised findings. Use when asked to review code, a diff, a branch or a file.
---
# Review code

Read-only by default: report findings; change code only if the user asks.

1. **Establish scope.** Identify the change under review (diff, branch, files) and its purpose. If unclear, review
   the uncommitted diff or ask.
2. **Understand context.** Read surrounding code, callers, tests and the project's conventions (`AGENTS.md`)
   so findings fit the existing architecture.
3. **Check, in priority order:** correctness and edge cases; error handling; security (input validation,
   authorisation, secrets, injection, path handling); concurrency and resource use; design fit and unnecessary
   complexity; tests; readability; documentation.
4. **Verify each finding** against the code. Prefer a concrete failure scenario (input → wrong result) over
   speculation. Drop findings you cannot support.
5. **Report** most severe first. For each: file and line, the problem, the scenario, and a suggested fix. Mark each
   as *blocking*, *suggestion* or *question*.
6. **Summarise** overall risk and whether the change is ready to merge.
