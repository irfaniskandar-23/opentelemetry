# Program.cs refactor, agent rules and hooks — design

Date: 2026-11-05 · Branch: `refactor/program-startup` (cut from `main` at `24a3e76`)

## Why

`Program.cs` is about 370 lines and mixes four kinds of things — configuration
reads, OpenTelemetry wiring, middleware and endpoints — with essay-length
comments between them. Finding one thing means scrolling past the other three.

Success looks like this:

- `Program.cs` reads as a table of contents. Each line is one call; Ctrl+click
  jumps to the file that does the work, and hovering shows a one-line
  `<summary>`.
- Every setting the app needs is listed and bound in one file.
- The decisions below are written as Claude Code rules, and the ones a script
  can check are enforced by hooks, so a future agent follows them without having
  seen this conversation.

This is not a roadmap phase. It changes the shape of the code, not what the app
does or what it sends to Better Stack.

## Goals

1. All configuration bound in one place, through the options pattern.
2. Wiring grouped by responsibility into extension methods, one file each.
3. Endpoints in a controller, with their logic unchanged.
4. Short, purposeful comments in every file.
5. A rule file for each decision, and two hooks.

## Non-goals

- **Startup try/catch with fatal logging.** Deferred by the owner, to be
  discussed in depth later. See "Deferred".
- Changing endpoint behaviour, or adding endpoints.
- Phase 9 work. It lives on `feature/phase-9-alerting` (`error.type`, the
  `/boom` `store.id` tag, `/simulate`) and meets this branch at merge time.
- Tests. The project has none by design.
- Moving explanations out of comments into new documents. The original spec
  already says phase notes, not code comments, are where concepts live; git
  history keeps the old comments.

## Anti-goals

Any of these makes the work a failure even if it compiles:

- What reaches Better Stack changes without anyone saying so: span names,
  `http.route`, span or resource attributes, log messages or log categories.
- Comments that replay the code, or quote documentation.
- A practice implemented from memory instead of from the current Microsoft
  Learn or opentelemetry.io page.

## Constraints

- .NET 10, ASP.NET Core. No new NuGet packages.
- Secrets stay in user secrets (`BetterStack:SourceToken`).
- Global conventions apply: feature branch, Conventional Commits, merge only,
  `dotnet build` passes before any push, `main` is protected.
- Docs first, with the link given: Microsoft Learn through the microsoft-docs
  plugin; OpenTelemetry through <https://opentelemetry.io/docs/languages/dotnet/>.
- **Execution is a checklist reviewed one item at a time.** For each item:
  explain briefly what it does and why (one concept, plain words), get the
  owner's go-ahead, implement, build, show the result, and wait for the owner's
  review before starting the next item. One commit per item.

## Decisions made by the owner

### 1. Configuration

- **One helper does the binding and validation**, so the chain is never
  repeated:
  ```csharp
  public static OptionsBuilder<T> AddValidatedOptions<T>(
      this IServiceCollection services, string sectionName)
      where T : class
      => services.AddOptions<T>()
          .BindConfiguration(sectionName)
          .ValidateDataAnnotations()
          .ValidateOnStart();
  ```
  It lives in its own file. Nothing else in the app calls `BindConfiguration`
  or `ValidateOnStart`.
- **One extension method, `AddConfiguration(this IServiceCollection)`**, in its
  own file, lists every setting the app needs: one `AddValidatedOptions<T>` line
  per section. Today that is `BetterStack` (`Endpoint`, `SourceToken`) and
  `Geocoding` (`BaseUrl`, `UserAgent`). It takes no `IConfiguration`, because
  `BindConfiguration` reads the app's configuration from DI itself.
- Each section binds to its own options class, which declares its section name
  as a `SectionName` constant and marks required values with `[Required]`.
- Values are validated at startup (`ValidateOnStart`), so a missing value stops
  the app before it serves a request.
- Everything else reads settings through `IOptions<T>`. No `Configuration["..."]`,
  `GetSection` or `GetValue` anywhere in the app.
- References:
  - Options pattern: <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0>
  - The helper's exact chain: <https://learn.microsoft.com/dotnet/core/extensions/options-library-authors#configuration-section-path-parameter>
  - `ValidateDataAnnotations` ships with the web SDK, no package needed:
    <https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options?view=aspnetcore-10.0#options-validation>
  - Grouping registrations in extension methods (its `AddConfig` example is the
    intended shape): <https://learn.microsoft.com/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0#register-groups-of-services-with-extension-methods>

### 2. Wiring grouped by responsibility

Separate extension methods, each in its own file, each reading options rather
than raw configuration:

- **OpenTelemetry**: resource, tracing, logging, and both OTLP exporters to
  Better Stack. Reads `BetterStackOptions`.
- **Geocoding**: the named `HttpClient`. Reads `GeocodingOptions`.

These are wiring, not settings, so they are not part of `AddConfiguration`.

### 3. Program.cs

A table of contents: create the builder, a short list of `Add...` calls, build,
a short list of `Use...` / `Map...` calls, run. No logic and no comments
(top-level statements cannot carry XML comments).

### 4. Controllers

- The four endpoints (`POST /stores`, `GET /stores/{id}`, `GET /stores`,
  `GET /stores/{id}/boom`) move into a `StoresController`. The logic inside each
  action stays as it is today, including the `CreateStore` span, its tags and
  the try/catch in create.
- Minimal API `Map{Verb}` endpoints are no longer used.

### 5. Comments (all files)

- In C#, comments are XML doc comments on types and members only. No `//`
  comments anywhere, including inside method bodies and in `Program.cs`. C#
  only allows XML doc comments directly above a type or member
  (<https://learn.microsoft.com/dotnet/csharp/language-reference/language-specification/documentation-comments>);
  anywhere else the compiler warns CS1587
  (<https://learn.microsoft.com/dotnet/csharp/misc/cs1587>).
- A comment says what the code is trying to achieve, never what it does line by
  line.
- Only for things that are not obvious.
- When code follows a known practice, link the docs page with
  `<see href="..."/>` instead of explaining it
  (<https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/recommended-tags#generate-links-and-references>).
  Never quote the docs.
- Under 20 words.

### 6. Rules

Each decision becomes its own Markdown file in `.claude/rules/`, written for an
agent that has not seen this conversation: what to do, what not to do, and the
doc link.

### 7. Hooks

Configured in the project's `.claude/settings.json`, which is committed, so
every clone gets them. Reference: <https://code.claude.com/docs/en/hooks>.

1. **Comment hook (first).** Before an `Edit` or `Write` to a `.cs` file, check
   the new text. If any line's first non-blank characters are `//` but not
   `///`, block the edit and tell the agent why, pointing to
   `.claude/rules/comments.md`.
   Accepted gap: a comment at the end of a code line (`x(); // ...`) is not
   caught, because `//` also appears inside strings such as URLs. The rule still
   covers it.
2. **Configuration hook (second).** Before an `Edit` or `Write` to a `.cs`
   file, block new text that:
   - contains `Configuration[`, `GetSection(` or `GetValue<` (in any file), or
   - contains `BindConfiguration(` or `ValidateOnStart(` in any file other than
     the `AddValidatedOptions` helper's file.

   The message points to `.claude/rules/configuration.md`. The hook checks
   *where* settings are read and bound, not whether a binding is right; that
   stays with the rule.

## Deferred: startup try/catch and fatal logging

Out of this round. What is known so far, so the later discussion can start from
it:

- Request failures never reach an outer try/catch. The exception handler
  middleware handles them, and failing that, Kestrel does. Anything the outer
  catch sees is a startup or shutdown failure.
  <https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0#server-exception-handling>
- The app's logger exists only once `builder.Build()` has run, and its Better
  Stack half cannot send if the Better Stack settings are what is missing.
  <https://learn.microsoft.com/dotnet/core/extensions/logging/overview#integration-with-hosts-and-dependency-injection>
- Open question: console only, or console plus Better Stack.

Until then a missing setting still stops the app, because `ValidateOnStart`
throws during startup; it just has no fatal log line of our own.

## Calls made by Claude (owner to check)

- **Folder layout.**
  ```
  src/OpenTelemetry.Api/
    Program.cs
    Configuration/   BetterStackOptions.cs, GeocodingOptions.cs
    Extensions/      OptionsExtensions.cs         AddValidatedOptions<T> (the one binding helper)
                     ConfigurationExtensions.cs   AddConfiguration
                     ObservabilityExtensions.cs   AddObservability (OpenTelemetry + Better Stack)
                     GeocodingExtensions.cs       AddGeocoding
                     TraceParentExtensions.cs     UseTraceParentHeader
    Controllers/     StoresController.cs
    Models/          Store.cs, CreateStoreRequest.cs, GeocodingMatch.cs
    Telemetry.cs, GlobalExceptionHandler.cs      (stay where they are)
  ```
  `AddObservability` rather than `AddOpenTelemetry`, because the SDK already has
  an `AddOpenTelemetry` on `IServiceCollection`.
- **Namespaces.** Extension classes go in `Microsoft.Extensions.DependencyInjection`,
  as the DI docs recommend, so `Program.cs` needs no `using`. Other types stay in
  the global namespace as they are today.
- **Traceparent middleware** moves into `UseTraceParentHeader()` so `Program.cs`
  holds no logic. Same behaviour, same position in the pipeline.
- **One-line registrations stay in `Program.cs`**: the store dictionary,
  `AddProblemDetails`, `AddExceptionHandler`, `AddControllers`.
- **Which members get a `<summary>`.** Every extension method gets one, stating
  its purpose, because that is what the hover in `Program.cs` shows. Other
  members only when something is not obvious.
- **`[ApiController]`** on `StoresController`, as Microsoft recommends for web
  APIs. Report any behaviour change it brings, such as automatic 400 responses.
- **Comment rule scope.** It also applies to the `.csproj` and the `.http` file
  (the owner said "all files"): the same purpose and length rules, each keeping
  its own comment syntax (`<!-- -->`, `#`). The hooks check `.cs` files only.
- **Hook scripts** are PowerShell 7 (`pwsh`) files in `.claude/hooks/`: built-in
  JSON parsing, no `jq`, and they run on Windows, macOS and Linux. Confirm on
  code.claude.com how hook commands are run on Windows and which tool names
  (`Edit`, `Write`, any others) carry file edits.
- **Rule files**: `comments.md`, `configuration.md`, `program-startup.md`,
  `controllers.md`. Scope each with `paths:` frontmatter if the current Claude
  Code docs support it (<https://code.claude.com/docs/en/memory>).
- **Docs.** Update the original spec's "A single ASP.NET Core minimal API
  throughout" and `CLAUDE.md`'s tech stack and layout to say controllers.
  `CLAUDE.md` gets one line under "Where we are now", and its Conventions section
  points to `.claude/rules/`. No phase note, since this is not a phase.

## Risks the builder must check

- **Span names and `http.route`** may change when endpoints move from minimal
  API routes to controller attribute routes. Take a baseline in Better Stack
  before the refactor, compare after, and use the Better Stack MCP tools rather
  than the UI. If anything differs, stop and tell the owner before going on.
- **Log category** for "Store found" and "Store not found" changes from
  `Program` to `StoresController`. Report it; the owner decides whether it
  matters.
- **Options inside OpenTelemetry and `HttpClient` setup.** Exporter and client
  configuration must read options from DI without building a second service
  provider early. Find the documented way: opentelemetry.io for the OTLP
  exporter; the `AddHttpClient` overloads that take an `IServiceProvider` on
  Microsoft Learn.
- **The comment hook applies to this refactor too.** Once it is on, any edit
  that carries an old `//` line is blocked. That is expected: it forces cleanup
  of whatever gets touched.
- **Phase 9 merge.** `feature/phase-9-alerting` also edits `Program.cs` and
  `GlobalExceptionHandler.cs`. Whichever branch merges second resolves the
  conflicts per the git-guardrails skill: explain both sides, wait for approval.

## Checklist

Each item is explained, approved, built and reviewed before the next begins.

1. Comment hook and the `comments.md` rule.
2. Baseline traces and logs in Better Stack; the `AddValidatedOptions` helper,
   options classes, `AddConfiguration` and the `configuration.md` rule.
3. Configuration hook.
4. `AddGeocoding`.
5. `AddObservability` (OpenTelemetry and the Better Stack exporters).
6. `UseTraceParentHeader`.
7. `StoresController`, the records moved into `Models/`, and the
   `controllers.md` rule.
8. `Program.cs` as a table of contents, and the `program-startup.md` rule.
9. Comment cleanup in files not yet touched: `Telemetry.cs`,
   `GlobalExceptionHandler.cs`, the `.csproj`, the `.http` file.
10. Docs: the original spec and `CLAUDE.md`.
11. Verification: `dotnet build`; run every request in the `.http` file; compare
    traces and logs in Better Stack against the baseline.
12. Pull request.
