---
name: refactor-code
description: Improve the structure of existing code without changing its behaviour, in small verified steps. Use when asked to refactor, clean up, simplify or restructure code.
---
# Refactor code

1. **Define the goal** (readability, duplication, coupling, testability) and the scope. Refactoring that crosses
   module boundaries, changes public APIs or introduces new patterns needs approval first.
2. **Secure behaviour:** make sure tests cover the code. If they do not, add characterisation tests first.
3. **Refactor in small steps** (rename, extract, inline, move, simplify conditionals), building and testing after
   each step.
4. **Keep behaviour identical,** including error messages, logging and performance characteristics unless the user
   asked otherwise.
5. **Follow existing conventions;** do not introduce a different architecture or style.
6. **Keep refactoring separate** from feature or bug-fix changes.
7. **Report** what changed structurally, why, and how behaviour preservation was verified.
