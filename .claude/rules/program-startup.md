---
paths:
  - "src/**/*.cs"
---

# Program startup

`Program.cs` is a table of contents: one call per line, in the order the pipeline needs. It holds no logic, no lambdas and no comments. Top-level statements cannot carry XML doc comments, so the file stays bare.

## Startup failure

The one exception is the try/catch around `app.StartAsync()`. A failed start logs one `Critical` line with the exception message only, so a developer sees the cause without a stack trace.

- Return exit code `1`; do not rethrow, or the runtime prints the stack trace again.
- Do not wrap `app.Run()`: it shuts logging down before the catch runs.
- `await using` on `app` sends logs still waiting when the program ends.
- `appsettings.json` raises `Microsoft.Extensions.Hosting.Internal.Host` to `Critical` to hide .NET's own stack-trace line.

## Adding wiring

- New service registrations go in an `Add{Group}` extension on `IServiceCollection` (namespace `Microsoft.Extensions.DependencyInjection`).
- New middleware goes in a `Use{Group}` extension on `IApplicationBuilder` (namespace `Microsoft.AspNetCore.Builder`).
- One file per responsibility in `src/OpenTelemetry.Api/Extensions/`, each with a one-line `<summary>`.
- One-line framework registrations such as `AddProblemDetails` and `AddControllers` may stay as direct calls.
- Settings: see `configuration.md`. Endpoints: see `controllers.md`.

## Order

Middleware order matters. `UseExceptionHandler()` comes first so it wraps everything after it.

## Never

- Put logic, lambdas or comments in `Program.cs`, other than the startup try/catch.
- Move logic or settings into an extension; extensions only register services or middleware.

## Links

- <https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0#register-groups-of-services-with-extension-methods>
- <https://learn.microsoft.com/aspnet/core/fundamentals/middleware/?view=aspnetcore-10.0#middleware-order>
