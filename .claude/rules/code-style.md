---
paths:
  - "src/**/*.cs"
---

# Code style

A method should read as a list of steps, without having to unpick nested or inline code to see what it does.

## Rules

- Methods and local functions always use a `{ }` block body, never `=>`. `.editorconfig` sets this as an error, and `dotnet build` fails (IDE0022, IDE0061) on any `=>` method because the csproj sets `EnforceCodeStyleInBuild`.
- A lambda whose body needs more than one line moves into a named private method, passed by name: `options.EnrichWithHttpRequest = TagRequestDetails`. One-line lambdas stay inline. The build cannot check this one.
- A string that names something outside the method moves into a `private const` at the top of the class: telemetry keys, span names, the service name, exporter paths.
- Constants use PascalCase, never UPPER_CASE, including private ones.
- A telemetry key's constant is the full key in PascalCase plus `Tag`: `"store.id"` is `StoreIdTag`, `"http.request.body.size"` is `HttpRequestBodySizeTag`.
- These stay inline: log messages, exception and error-response text, route templates in `[Route]` and `[Http*]`, and `$"..."` strings built around a value.

The rules apply however a file is changed.

## Links

- <https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/ide0022>
- <https://learn.microsoft.com/dotnet/core/project-sdk/msbuild-props#enforcecodestyleinbuild>
- <https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/identifier-names#naming-conventions>
