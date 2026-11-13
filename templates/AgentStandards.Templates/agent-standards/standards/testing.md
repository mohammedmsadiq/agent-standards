# Testing

## Expectations

- New behaviour comes with tests. Bug fixes come with a test that fails before the fix.
- Run the relevant tests before reporting work as done, and report the actual result.
- Use the project's existing test framework, assertion style, naming and folder conventions. Do not introduce a
  second test framework.

## Good tests

- Test behaviour through public interfaces, not implementation details.
- One reason to fail per test; descriptive names that state the scenario and expected outcome.
- Arrange–Act–Assert (or Given–When–Then) structure.
- Deterministic: no dependence on wall-clock time, randomness, network, ordering or shared state without control.
- Fast by default; isolate slow integration and end-to-end tests so they can be run separately.

## Test levels

- **Unit tests** for domain logic and edge cases.
- **Integration tests** for boundaries: database, file system, HTTP, messaging — use real implementations or
  containers where practical, and test doubles only where needed.
- **End-to-end or UI tests** for critical user journeys only.

## Test doubles

Prefer fakes and in-memory implementations over deep mocking. Mock only what you own or what is slow or
non-deterministic. Never mock the code under test.

## Coverage

Coverage is a signal, not a goal. Cover the important paths, error handling and boundaries. Do not write tests
that only exercise code without asserting behaviour.
