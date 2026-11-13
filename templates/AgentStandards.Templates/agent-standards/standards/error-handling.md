# Error handling

- **Fail fast on programming errors.** Validate arguments and invariants; throw or return errors early with clear
  messages.
- **Handle expected failures explicitly.** Network, I/O, validation and concurrency failures are normal: model
  them with the project's chosen mechanism (exceptions, result types, error values) and handle them where a
  meaningful decision can be made.
- **Never swallow errors.** An empty catch block or ignored error value needs a comment explaining why it is safe.
- **Preserve context.** Wrap or rethrow with additional context; keep the original cause and stack trace.
- **Catch specific errors**, not the broadest base type, except at top-level boundaries.
- **Boundaries translate errors.** At APIs and UI edges, convert internal errors into safe, user-appropriate
  responses (for example standard problem-details payloads) without leaking stack traces or secrets.
- **Clean up resources** deterministically (`using`, `try/finally`, `with`, `defer`).
- **Retries** only for transient failures, with limits, backoff and idempotency in mind.
- **Log once**, at the boundary that handles the error, with enough context to diagnose it (see
  [Logging and observability](logging-observability.md)).
