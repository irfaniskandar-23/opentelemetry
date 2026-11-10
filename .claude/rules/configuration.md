---
paths:
  - "src/**/*.cs"
  - "**/appsettings*.json"
---

# Configuration

Every setting reaches the app as a validated, strongly typed options class. A missing value stops the app at startup instead of failing later inside a request or a background exporter.

## Adding a setting

1. Put the value in `appsettings.json`. If it is a credential, use user secrets instead (`dotnet user-secrets set "Section:Key" "value" --project src/OpenTelemetry.Api`), never `appsettings*.json`.
2. Add or extend an options class in `src/OpenTelemetry.Api/Configuration/`: a `public const string SectionName`, and `[Required]` on every value the app cannot run without (see `GeocodingOptions`). Its message names the full key, built from constants so a rename cannot leave it stale: `[Required(ErrorMessage = $"{SectionName}:{nameof(BaseUrl)} is required.")]`. Say what is missing, not where to set it; the value can come from any configuration source.
3. Register it with one line inside `AddConfiguration` (`Extensions/ConfigurationExtensions.cs`): `services.AddValidatedOptions<MyOptions>(MyOptions.SectionName);`
4. Consume it by injecting `IOptions<T>`, or by resolving it from the `IServiceProvider` inside a registration callback. To configure another library's options from yours, use `services.AddOptions<TOther>(name).Configure<IOptions<T>>(...)`.

## Never

- Read `IConfiguration["..."]`, `GetSection` or `GetValue`.
- Call `BindConfiguration`, `Bind`, `ValidateDataAnnotations` or `ValidateOnStart` anywhere except `Extensions/OptionsExtensions.cs`.
- Take an `IConfiguration` parameter in an extension method.

A hook blocks raw configuration reads in any `.cs` file, and `BindConfiguration` / `ValidateOnStart` outside `Extensions/OptionsExtensions.cs`.

The hook may also block `JsonNode.GetValue<T>()` because it matches `GetValue<`; use `JsonElement` or a typed record instead.

The rule applies however a file is changed; the hook only sees Edit/Write, not shell edits.

## Links

- <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0>
- <https://learn.microsoft.com/dotnet/core/extensions/options-library-authors#configuration-section-path-parameter>
- <https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0#register-groups-of-services-with-extension-methods>
