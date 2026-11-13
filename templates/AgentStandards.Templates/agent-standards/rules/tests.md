---
description: Conventions for writing and changing automated tests.
globs: ["**/test/**", "**/tests/**", "**/*.test.*", "**/*.spec.*", "**/*Tests/**", "**/*Tests.cs", "**/*Test.cs", "**/test_*.py", "**/*_test.go"]
---
# Test files

- Use the framework, assertion and mocking libraries already used by neighbouring tests; do not add new ones.
- Name tests by scenario and expected outcome, and structure them Arrange–Act–Assert.
- Keep tests deterministic: no real clock, network, randomness or shared mutable state without control.
- A bug fix includes a test that fails without the fix.
- Run the affected tests and report the actual result.
