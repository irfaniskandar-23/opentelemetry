---
paths:
  - "src/**/*.cs"
---

# Program startup

`Program.cs` is a table of contents: one call per line, in the order the pipeline needs. It holds no logic, no lambdas and no comments. Top-level statements cannot carry XML doc comments, so the file stays bare.

## Adding wiring

- New service registrations go in an `Add{Group}` extension on `IServiceCollection` (namespace `Microsoft.Extensions.DependencyInjection`).
- New middleware or endpoint mapping goes in a `Use{Group}` extension on `IApplicationBuilder` (namespace `Microsoft.AspNetCore.Builder`).
- One file per responsibility in `src/OpenTelemetry.Api/Extensions/`, each with a one-line `<summary>`.
- One-line framework registrations such as `AddProblemDetails` and `AddControllers` may stay as direct calls.
- Settings: see `configuration.md`. Endpoints: see `controllers.md`.

## Order

Middleware order matters. `UseExceptionHandler()` comes first so it wraps everything after it.

## Never

- Put logic, lambdas or comments in `Program.cs`.
- Add a new extension for something that is not wiring. Report it instead.

## Links

- <https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0#register-groups-of-services-with-extension-methods>
- <https://learn.microsoft.com/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0#middleware-order>
