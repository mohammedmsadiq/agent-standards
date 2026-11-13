---
name: write-tests
description: Write or improve automated tests for existing or new code using the project's own test framework and conventions. Use when asked to add tests, increase coverage or test a specific behaviour.
---
# Write tests

1. **Discover the conventions:** test framework, assertion and mocking libraries, folder layout, naming and how
   tests are run. Do not introduce a new framework.
2. **Decide what to test:** public behaviour, edge cases, error paths and boundaries. Prioritise risky and
   frequently changed code.
3. **Choose the level:** unit tests for logic; integration tests for real boundaries (database, file system, HTTP)
   where the project supports them.
4. **Write clear tests:** scenario-based names, Arrange–Act–Assert, one reason to fail, deterministic (control
   time, randomness and I/O), no logic in tests.
5. **Use test doubles sparingly:** prefer fakes over deep mocks; never mock the code under test.
6. **Run the tests** and make them pass for the right reason. If a test reveals a real bug, report it rather than
   weakening the test.
7. **Report** what is covered, what is not, and how to run the tests.
