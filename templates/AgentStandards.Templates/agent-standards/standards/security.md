# Security

## Secrets

- Never hard-code, commit, log or print secrets, tokens, keys, connection strings or personal data.
- Load secrets from the platform's secret store or environment configuration; use placeholders in examples.
- If a secret is exposed, stop and tell the user: it must be rotated, not just deleted from the latest commit.
- Handling credentials always requires explicit approval.

## Input and output

- Treat all external input as untrusted: validate type, length, range and format at the boundary.
- Use parameterised queries or the ORM's safe APIs; never build SQL, shell commands or paths by string
  concatenation with untrusted data.
- Encode output for its context (HTML, URLs, JSON, logs) to prevent injection.
- Canonicalise and validate file paths; reject traversal (`..`) and unexpected symbolic links.

## Authentication and authorisation

- Use the framework's established authentication mechanisms; never implement custom cryptography.
- Enforce authorisation on the server side for every protected operation; deny by default.
- Changes to authentication, authorisation, session handling or security headers need approval and review.

## Data protection

- Collect and keep only the data you need. Protect personal data in transit (TLS) and at rest where required.
- Do not log personal data or secrets; mask identifiers where logs need correlation.

## Dependencies and supply chain

- Keep dependencies current and audited (see [Dependency management](dependency-management.md)).
- Do not download and execute code from untrusted sources during builds or at runtime.

## Reviews

Use the `review-security` skill or the `security-reviewer` agent for changes that touch the areas above.
