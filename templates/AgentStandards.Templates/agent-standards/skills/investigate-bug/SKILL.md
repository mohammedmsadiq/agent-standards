---
name: investigate-bug
description: Systematically reproduce, locate and explain the root cause of a bug, then propose or implement a minimal fix with a regression test. Use for errors, crashes, failing tests or unexpected behaviour.
---
# Investigate a bug

1. **Capture the symptom precisely:** expected vs actual behaviour, error messages, stack traces, environment,
   and when it started (recent changes, versions).
2. **Reproduce it.** Find the smallest reliable reproduction — ideally an automated test that fails. If you cannot
   reproduce it, say so and list what you tried.
3. **Form hypotheses** from evidence (logs, stack trace, recent diffs, data) and test them one at a time. Use
   version-control history to find when behaviour changed.
4. **Find the root cause,** not just the line that throws. Explain the chain from cause to symptom.
5. **Fix minimally** in line with the existing design. Ask before fixes that need architecture changes, data
   migrations or dependency changes.
6. **Prove it:** the reproducing test now passes and the related test suite still passes.
7. **Report** root cause, fix, verification, and any related risks or similar code that may share the bug.
