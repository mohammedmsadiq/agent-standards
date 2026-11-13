---
name: design-api-endpoint
description: Design or review an HTTP API endpoint and its contract — resource model, methods, status codes, validation, errors, versioning and documentation. Use when adding or changing REST API endpoints.
---
# Design an API endpoint

1. **Model the resource** and operation; choose the HTTP method and route consistent with existing endpoints.
2. **Define the contract:** request and response DTOs separate from domain models, naming conventions, required and
   optional fields, formats (ISO 8601 UTC dates), and bounds.
3. **Define responses:** success codes, problem-details errors for validation, not-found, conflict and
   authorisation failures.
4. **Security:** authentication and authorisation policy, input validation, rate limiting if public.
5. **Compatibility:** confirm the change is additive; otherwise propose a versioning approach and get approval.
6. **Implement and test** following the existing endpoint style; add tests for success and failure cases.
7. **Document** with OpenAPI metadata and examples.
