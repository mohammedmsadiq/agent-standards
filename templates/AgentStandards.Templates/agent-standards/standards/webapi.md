# REST APIs

Applies to HTTP APIs in any stack. Extends the ASP.NET Core standards when the project uses ASP.NET Core.

## Design

- Model resources with nouns and standard HTTP methods; use correct status codes (`201` with `Location` on
  create, `204` for no content, `400`/`422` for validation, `404`, `409` for conflicts).
- Keep request/response contracts (DTOs) separate from persistence and domain models.
- Use consistent naming (camelCase JSON unless the project differs), ISO 8601 dates in UTC, and explicit units.
- Support pagination, filtering and sorting for collections; never return unbounded result sets.
- Make `PUT`/`DELETE` idempotent; support idempotency keys for retried `POST` operations where needed.

## Errors

Return RFC 9457 problem details (`application/problem+json`) with a stable `type`, human-readable `title`,
and a trace identifier. Never return stack traces or internal messages.

## Compatibility and versioning

- Treat the published contract as a promise: additive changes are safe; removals, renames and semantic changes are
  breaking and need a versioning plan and approval.
- Follow the project's existing versioning scheme (URL segment, header or media type).

## Documentation

Keep an OpenAPI description generated from code (the built-in OpenAPI support in ASP.NET Core where used) and
include examples. Document authentication requirements per endpoint.

## Security

Authenticate every non-public endpoint, authorise per resource, validate and bound all input (size, ranges,
formats), apply rate limiting where exposed publicly, and avoid exposing internal identifiers that enable
enumeration where that matters.
