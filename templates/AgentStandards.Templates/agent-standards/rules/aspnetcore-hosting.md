---
description: Rules for ASP.NET Core startup, configuration and middleware.
globs: ["**/Program.cs", "**/Startup.cs", "**/appsettings*.json"]
---
# ASP.NET Core hosting and configuration

- Middleware order matters; keep exception handling first and authentication before authorisation.
- Never put secrets in `appsettings*.json`; use user secrets, environment variables or a vault.
- Changes to authentication, authorisation, CORS, HTTPS or security headers require approval.
- Bind configuration with the options pattern and validate it at startup.
