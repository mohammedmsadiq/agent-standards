---
name: review-security
description: Review code, configuration or a change for security vulnerabilities and unsafe practices, and report prioritised, verified findings. Use for security reviews and for changes touching authentication, input handling, secrets or data access.
---
# Security review

Read-only: report findings; do not change code unless asked. Never exercise attacks against live systems.

1. **Map the attack surface:** entry points (APIs, UI, files, messages, CLI), trust boundaries, sensitive data and
   privileged operations.
2. **Check:**
   - input validation and output encoding (injection: SQL, command, path, template, XSS);
   - authentication and authorisation on every protected operation (deny by default, no client-side trust);
   - secrets in code, configuration, logs or history;
   - cryptography (use platform libraries; no custom crypto; strong algorithms);
   - deserialisation, file uploads, path traversal and symbolic links;
   - dependency vulnerabilities and supply-chain risks;
   - security headers, CORS, CSRF/antiforgery, TLS;
   - logging of sensitive data and error disclosure.
3. **Verify** each finding with a concrete exploit scenario based on the code. Rate severity by impact and
   likelihood.
4. **Report** most severe first with location, scenario, impact and remediation. If you find an exposed secret,
   say it must be rotated.
