---
description: Rules for HTTP API endpoints, controllers and contracts.
globs: ["**/Controllers/**", "**/Endpoints/**", "**/Contracts/**", "**/*Dto.cs", "**/*Request.cs", "**/*Response.cs"]
---
# API endpoints and contracts

- Use correct HTTP methods and status codes; return problem details for errors.
- Validate and bound all input; authorise every non-public endpoint.
- Treat published contracts as stable: removals and renames are breaking changes that need approval.
- Keep OpenAPI documentation accurate, including authentication requirements.
