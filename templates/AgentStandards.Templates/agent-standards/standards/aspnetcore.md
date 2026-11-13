# ASP.NET Core applications

Extends the [.NET standards](dotnet.md).

## Hosting and configuration

- Configure services and the middleware pipeline in `Program.cs` (or the project's established structure).
  Middleware order matters: exception handling, HTTPS/HSTS, static files, routing, CORS, authentication,
  authorisation, then endpoints.
- Use the options pattern (`IOptions<T>`) with validation for configuration; keep secrets in user secrets,
  environment variables or a vault — never in `appsettings*.json` committed to source.

## Security

- Require HTTPS; enable HSTS outside development.
- Use the built-in authentication and authorisation; apply authorisation policies to endpoints, deny by default.
- Enable antiforgery protection for cookie-authenticated form posts.
- Configure CORS explicitly with the narrowest origins needed.
- Validate all model input; return problem details, not exception details, to clients.

## Reliability

- Use `IHttpClientFactory` and the built-in resilience handlers for outgoing calls.
- Use health checks (`MapHealthChecks`) that reflect real dependencies.
- Use `IHostedService`/`BackgroundService` for background work, honouring cancellation on shutdown.

## Data access

- Use the project's data-access approach consistently. For Entity Framework Core, keep `DbContext` scoped,
  avoid N+1 queries, use `AsNoTracking` for read-only queries, and review generated migrations before applying.
  Applying migrations to shared or production databases needs approval.

## Observability

Use `ILogger`, built-in metrics and OpenTelemetry when configured. Include trace identifiers in error responses.
