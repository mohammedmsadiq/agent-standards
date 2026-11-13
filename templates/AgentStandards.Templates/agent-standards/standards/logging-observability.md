# Logging and observability

## Logging

- Use the project's logging abstraction; do not write directly to the console in application code unless that is
  the project's convention.
- Use structured logging with named properties rather than string concatenation.
- Choose levels deliberately: errors need attention; warnings are unexpected but handled; information records
  significant business events; debug/trace are for diagnosis and off by default in production.
- Include correlation identifiers to trace a request across components.
- Never log secrets, tokens, passwords or personal data. Mask identifiers when needed.
- Avoid logging in tight loops; consider sampling.

## Metrics and tracing

- Prefer open standards such as OpenTelemetry where the project supports them.
- Measure what matters to users: latency, error rate, throughput, saturation.
- Name metrics and spans consistently, with low-cardinality attributes.

## Health and diagnostics

- Services expose health checks that reflect real dependencies.
- Errors surfaced to users have a reference that support staff can find in logs.
