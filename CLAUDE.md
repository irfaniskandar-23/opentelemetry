# CLAUDE.md

## Purpose

A learning project for distributed tracing and OpenTelemetry, built in seven
phases, one concept at a time. The goal is understanding, not a finished product.

It answers three problems from real work: not knowing which client caused a
failed request, not being able to diagnose failures that depend on the payload,
and having no structured way to attach business context such as a store ID to an
operation.

Full detail lives in
`docs/superpowers/specs/2026-08-04-distributed-tracing-learning-project-design.md`.
That spec is the source of truth. Read it before proposing work.

## Current state

**Phase 5 complete.** The store domain (`POST /stores`, `GET /stores/{id}`,
`GET /stores`) sits over an in-memory dictionary. `POST /stores` starts a
`CreateStore` child span from the application's own `ActivitySource`
(`Telemetry.cs`), tagged with `store.id` and `store.name`.
`GET /stores/{id}/boom` throws, and `GlobalExceptionHandler` (an
`IExceptionHandler`) marks `Activity.Current` as `Error`, records the exception
on it, and returns RFC 9457 ProblemDetails. Every response carries a
`traceparent` header: an inline middleware writes it on the success path, and
`GlobalExceptionHandler` writes it again on the error path because
`UseExceptionHandler` calls `Response.Clear()` in between.

The hand-written `ActivityListener` is gone. The OpenTelemetry SDK replaces it:
`AddSource(Telemetry.SourceName)` for the application's own spans,
`AddAspNetCoreInstrumentation()` (with an `EnrichWithHttpRequest` hook) for the
request span, a resource carrying `service.name` and
`deployment.environment.name`, and an OTLP exporter over HTTP/protobuf to Better
Stack. The endpoint lives in `appsettings.json`; the source token lives in user
secrets. Two csproj lines — `PackageId` and `AssemblyName`, both `StoreApi` —
resolve the collision between this project's name and the real
`OpenTelemetry.Api` package; nothing else is renamed. Notes:
`docs/phase-1-activity.md`, `docs/phase-2-custom-spans.md`,
`docs/phase-3-exceptions.md`, `docs/phase-4-traceparent.md`,
`docs/phase-5-otel-sdk.md`.

Next: phase 6 — the network hop. `IHttpClientFactory` and
`AddHttpClientInstrumentation()`, calling Nominatim to geocode the submitted
address, where `CreateStore` gains its first child span and the `traceparent` is
injected on the way out without a line of code. No second service: the hop is to
a real third party. Phase 6.1 then adds the connection setup activity.

Update this section in every phase's PR.

## Working agreement

This section matters more than the rest of the file. The owner is learning this
material and has abandoned similar projects before through feeling overwhelmed.

- **Explain before implementing.** Describe what a change does and why it works
  before writing it. Unexplained working code is a failed phase.
- **One phase at a time.** Do not start the next phase because the current one
  went quickly.
- **No forward leakage.** Never introduce a technique from a later phase.
  Specifically, do not add OpenTelemetry packages before phase 5 or an
  `HttpClient` before phase 6 — phases 1 through 4 install nothing at all, and
  discovering that tracing is native to .NET is the point of those phases.
- **Smallest change that demonstrates the concept.** Resist adding endpoints,
  abstractions, or layers that do not teach something new.
- **Ask rather than assume** when a decision would change what gets learned.

## Tech stack

- .NET 10, ASP.NET Core minimal API, C# with nullable enabled.
- Storage: an in-memory `ConcurrentDictionary` singleton. No database.
- No test project. Verification is manual through
  `src/OpenTelemetry.Api/OpenTelemetry.Api.http`.
- OpenTelemetry SDK plus an OTLP exporter to Better Stack, from phase 5 onward.
- Secrets go in user secrets, never in `appsettings.json`.

## Layout

```
src/OpenTelemetry.Api            StoreApi   — the only service
docs/phase-N-<topic>.md          one note per phase; concepts live here
docs/superpowers/specs/          the design spec
```

## Domain

Store onboarding. `Store { Id, Name, Address, Latitude, Longitude, CreatedAt }`.

Endpoints: `POST /stores`, `GET /stores/{id}`, `GET /stores`, and
`GET /stores/{id}/boom`, which throws deliberately. `PUT` and `DELETE` are
excluded on purpose — they teach nothing new about tracing.

## Phase roadmap

| # | Phase | Teaches | Status |
|---|---|---|---|
| 1 | Activity, observed | What .NET already records, with zero packages | Done |
| 2 | Custom spans and attributes | Attaching `store.id` to an operation | Done |
| 3 | Exceptions and ProblemDetails | `IExceptionHandler`, errors on the span | Done |
| 4 | `traceparent` on the response | W3C Trace Context, header format | Done |
| 5 | OpenTelemetry SDK and Better Stack | Exporting via OTLP | Done |
| 6 | The network hop | Automatic client spans and `traceparent` injection | Not started |
| 6.1 | Connection cost | Links versus parent-child; shared resources | Not started |
| 7 | Logs and trace correlation | Generic messages, structured properties | Not started |

## Conventions

Global conventions in `~/.claude/CLAUDE.md` apply in full. The ones that come up
most here:

- Feature branch per phase: `feature/phase-N-<topic>`. Never commit or push
  directly to `main`, which is protected by a ruleset.
- Conventional Commits.
- `dotnet build` must pass before any push.
- Merge commits only, never rebase.
- Every phase ends with a notes file and a PR that updates the current-state
  section above.
