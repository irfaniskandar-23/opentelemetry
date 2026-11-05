---
paths:
  - "src/**/*.cs"
---

# Controllers

Every HTTP endpoint is an action on a controller. `Program.cs` stays a table of contents and holds no endpoint logic.

## Adding an endpoint

1. Put the action in a controller under `src/OpenTelemetry.Api/Controllers/`. A controller is `public sealed`, derives from `ControllerBase`, and carries `[ApiController]` and `[Route("...")]`.
2. Mark the action with `[HttpGet]`, `[HttpPost]` and so on. Keep route constraints such as `{id:guid}`.
3. Take dependencies through the primary constructor (`ILogger<TController>`, `IHttpClientFactory`, singletons). Do not use `[FromServices]` or service location.
4. Put request and response types in `src/OpenTelemetry.Api/Models/`, one `public` type per file. A public controller cannot expose internal types.

## Never

- Add `app.MapGet`, `app.MapPost` or any other `app.Map{Verb}` endpoint. `app.MapControllers()` is the only mapping call.
- Put endpoint logic in `Program.cs`.
- Rewrite span names or `http.route` in code to hide a routing change. Report the change instead.

## Telemetry

Changing a controller's route template or name changes span names, `http.route` and the log category (`ILogger<T>`). Check Better Stack before and after.

## Links

- <https://learn.microsoft.com/aspnet/core/web-api/?view=aspnetcore-10.0>
- <https://learn.microsoft.com/aspnet/core/mvc/controllers/routing?view=aspnetcore-10.0>
