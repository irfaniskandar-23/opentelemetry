# Distributed Tracing Learning Project — Design

**Date:** 2026-08-04
**Status:** Approved, not yet implemented

## Purpose

Learn distributed tracing and OpenTelemetry by building one small system in seven
phases, one concept at a time.

The project exists to answer specific problems encountered at work:

1. **Unknown request origin.** When an endpoint fails, there is no identifier
   linking the failure back to the client that caused it. Investigation starts
   from the endpoint path, which only helps when the whole endpoint is down.
2. **Unpredictable, payload-dependent failures.** An endpoint that fails for some
   requests and not others cannot be diagnosed without asking the caller for a
   sample payload.
3. **No structured way to attach business context.** Searching logs for a raw
   value such as a store ID is the current workaround.

Distributed tracing solves all three. This project demonstrates how, in code, in
increments small enough to finish.

## Design principles

These constrain every phase and override any instinct to build more:

- **One concept per phase.** A phase introduces exactly one new idea.
- **Explain before implementing.** The point is understanding, not a finished repo.
- **Every phase runs.** Stopping after any phase leaves a working application.
- **Smallest change that demonstrates the concept.** No feature earns its place
  unless it teaches something not already taught.
- **No forward leakage.** Phase N does not use techniques from phase N+1.

## Architecture

A single ASP.NET Core minimal API for phases 1–5. A second service appears in
phase 6, when a network hop is needed.

```
src/
  OpenTelemetry.Api           StoreApi   — the main service (phases 1–7)
  OpenTelemetry.GeocodingApi  Geocoding  — created in phase 6 only
docs/
  phase-1-activity.md         one note per phase
  ...
```

### Domain

Store onboarding. Chosen because the outbound call in phase 6 is on the critical
path, fails per-payload rather than globally, and is slow — the three properties
that make a trace worth reading.

```csharp
record Store(
    Guid Id,
    string Name,
    string Address,
    double? Latitude,
    double? Longitude,
    DateTimeOffset CreatedAt);
```

Storage is a `ConcurrentDictionary<Guid, Store>` registered as a singleton. No
database. Persistence is not a topic this project teaches, and a database would
add a second span type before the first one is understood.

### Endpoints

| Endpoint | Purpose |
|---|---|
| `POST /stores` | Write path. Carries business attributes; gains the geocoding hop in phase 6. |
| `GET /stores/{id}` | Read path. Mirrors the "a GET failed and I don't know whose it was" problem. |
| `GET /stores` | List. Kept because a trivially fast span is a useful contrast in a waterfall. |
| `GET /stores/{id}/boom` | Throws deliberately. Introduced in phase 3, retained thereafter. |

`PUT` and `DELETE` are deliberately excluded. They teach nothing about tracing
that `POST` and `GET` do not already teach.

### Data flow, final state

```
Client
  |  traceparent (optional; generated if absent)
  v
StoreApi  POST /stores
  |-- validate                                    child span
  |-- GET /geocode  ---> GeocodingApi             client span + server span
  |                        |-- lookup             child span (may throw)
  |-- save to dictionary                          child span, attr store.id
  v
Response  201 Created  +  traceparent header
   (on failure: RFC 9457 ProblemDetails including traceId)
```

## Phases

Each phase is one branch, one PR, one merge commit into `main`, and one notes
file in `docs/`.

### Phase 1 — Activity, observed

**Question answered:** what is .NET already recording?
**Dependencies added:** none.

Replace the weatherforecast template with `POST /stores`, `GET /stores/{id}` and
`GET /stores`. `Latitude` and `Longitude` remain null until phase 6.

Register an `ActivityListener` (roughly 20 lines) that prints each activity's
start and stop to the console: display name, `TraceId`, `SpanId`, `ParentId`,
kind, duration, and tags.

**Verify:** send `POST /stores` from the `.http` file, observe a span. Then send
the same request with a hand-written `traceparent` header and observe that
`TraceId` matches the value sent and `ParentId` is populated. This demonstrates
W3C Trace Context with no library involved.

**Note covers:** what an `Activity` is, that ASP.NET Core creates one per request
with no configuration, and that OpenTelemetry has not entered the picture.

### Phase 2 — Custom spans and attributes

**Question answered:** how is business context attached to an operation?
**Dependencies added:** none.

Introduce an `ActivitySource` owned by the application. Wrap the save operation
in a child span. Attach `store.id` and `store.name` as tags.

**Verify:** the console shows a parent request span with a nested save span, and
`store.id` appears as a tag rather than inside a message string.

**Note covers:** attributes versus string interpolation; semantic-convention
naming (`http.request.method` is standardised, `store.id` is application-specific
— lowercase, dot-separated); why personally identifying data does not belong in
attributes.

### Phase 3 — Exceptions and ProblemDetails

**Question answered:** should there be a global exception handler, and how does an
exception reach the trace?
**Dependencies added:** none.

Yes to a global handler — but not hand-written middleware. Use `IExceptionHandler`
together with `AddProblemDetails()`. The handler must:

1. Set the current activity's status to `Error` with a description.
2. Record the exception on the activity via `AddException`.
3. Return an RFC 9457 ProblemDetails response.

Add `GET /stores/{id}/boom` as the trigger.

The handler reads `Activity.Current` rather than receiving a trace ID from
elsewhere. This is the phase that replaces the "middleware passes the trace ID
along the pipeline" mental model with the real one: the activity is ambient and
flows automatically via `AsyncLocal`.

**Verify:** calling `/boom` returns a ProblemDetails body, and the console span
shows status `Error` with exception details attached.

**Note covers:** why ambient context removes the need for manual propagation;
what ProblemDetails is and why a consistent error contract matters.

### Phase 4 — traceparent on the response

**Question answered:** what is the `traceparent` header, and how is it returned to
a caller?
**Dependencies added:** none.

Middleware writes the current activity's `traceparent` to the response headers.
The ProblemDetails payload also gains a `traceId` extension so a failing client
can quote it in a bug report.

**Verify:** every response carries a `traceparent` header whose trace ID matches
the console output.

**Note covers:** full dissection of
`00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01` — version, trace ID,
parent span ID, flags; what `tracestate` is and why it is not needed here;
sampling flags.

### Phase 5 — OpenTelemetry SDK and Better Stack

**Question answered:** how does this data leave the machine?
**Dependencies added:** `OpenTelemetry.Extensions.Hosting`,
`OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`.

Remove the hand-written `ActivityListener`. Register the OpenTelemetry SDK with
ASP.NET Core instrumentation, the application's `ActivitySource`, and an OTLP
exporter pointed at Better Stack. Configure a service name via `ResourceBuilder`.

Nothing built in phases 1–4 changes. That is the lesson: one listener was swapped
for another. The instrumentation was never OpenTelemetry-specific because .NET
creates the activities, and OpenTelemetry only collects them.

Credentials live in user secrets, never in `appsettings.json`.

**Verify:** a trace from `POST /stores` appears in Better Stack with the same
trace ID printed locally, including custom attributes and error status.

**Note covers:** the relationship between `Activity`, `ActivitySource`,
`ActivityListener` and the OpenTelemetry SDK; what OTLP is; what a Resource is.

**Risk:** this is the only phase depending on an external account. If Better Stack
proves difficult, substitute a local Jaeger container — conceptually identical, as
both consume OTLP. No other phase is affected.

### Phase 6 — The network hop

**Question answered:** are outbound I/O calls instrumented manually, or is it
automatic?
**Dependencies added:** `OpenTelemetry.Instrumentation.Http`; new project
`src/OpenTelemetry.GeocodingApi`.

GeocodingApi exposes `GET /geocode?address=...`, holds a hardcoded map of a few
known addresses, applies an artificial delay of several hundred milliseconds, and
throws for unknown addresses. It registers the OpenTelemetry SDK the same way
StoreApi does.

StoreApi calls it via `IHttpClientFactory` during `POST /stores` and stores the
returned coordinates. A geocoding failure fails the request.

The answer to the phase question is that it is automatic:
`AddHttpClientInstrumentation()` creates the client span and injects the
`traceparent` header, and the receiving service continues the trace with no code
on either side. The work in this phase is proving that, then adding only the
attributes the library cannot infer.

**Verify:** one trace spans both processes. A known address produces a successful
waterfall dominated by the geocoding call; an unknown address produces a failed
trace where the error is visible in GeocodingApi while the request under
investigation was made against StoreApi.

**Note covers:** span kinds (server, client, internal); context propagation over
HTTP; why the injected header is the same standard typed by hand in phase 1.

### Phase 7 — Logs and trace correlation

**Question answered:** should log messages carry business specifics, or stay
generic?
**Dependencies added:** none beyond phase 5 — OpenTelemetry's logging provider
ships in `OpenTelemetry.Extensions.Hosting`. Serilog is deliberately not used;
adding a logging library would obscure the point, which is that `ILogger` and the
ambient activity already correlate without help.

Add `ILogger` calls to the store endpoints, export logs via OpenTelemetry, and
demonstrate that each log record carries the current trace and span IDs.

The rule demonstrated: log messages stay generic with structured properties
attached; business identity lives in span attributes; correlation is automatic
through the trace ID. Finding one store's failure means filtering traces by
`store.id`, then reading that trace's logs — never text-searching logs for a
raw value.

**Verify:** in Better Stack, a trace can be opened and its log records viewed
alongside it.

## Verification approach

Manual, through `src/OpenTelemetry.Api/OpenTelemetry.Api.http`. Each phase defines
concrete success criteria in the form "send this request, observe this output".

No automated test project. The deliverable of each phase is understanding, which
is confirmed by looking at a trace rather than by an assertion. Testing that
instrumentation has not regressed is a real technique worth learning later, but it
is a third topic on top of tracing and logging and would work against the
one-concept-per-phase constraint.

## Documentation

One notes file per phase in `docs/`, named `phase-N-<topic>.md`. Each records what
was built, what to look for when running it, and the concepts that phase answers.
These, not code comments, are where conceptual explanation lives.

`CLAUDE.md` holds current state, working agreement and conventions, and points
here for detail.

## Workflow

Per the global conventions in `~/.claude/CLAUDE.md`:

- Feature branch `feature/phase-N-<topic>`; never commit directly to `main`.
- Conventional Commits.
- `dotnet build` must pass before any push.
- Merge commit PRs; never rebase.
- Each phase's PR updates the current-phase marker in `CLAUDE.md`.

## Out of scope

- Metrics. Better Stack supports them, but tracing is the subject here.
- Databases, authentication, and validation frameworks.
- Sampling strategy beyond understanding the sampled flag.
- Automated tests.
- Browser or frontend instrumentation.
- `PUT` and `DELETE` endpoints.
