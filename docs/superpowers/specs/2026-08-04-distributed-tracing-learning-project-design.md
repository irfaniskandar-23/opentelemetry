# Distributed Tracing Learning Project — Design

**Date:** 2026-08-04
**Status:** Approved, not yet implemented

## Purpose

Learn distributed tracing and OpenTelemetry by building one small system in
phases, one concept at a time. Phases 1–7 were designed together; later phases
are added here only once each is designed.

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

A single ASP.NET Core minimal API throughout. The network hop in phase 6 is a
call to a third-party service, so no second project is ever created.

```
src/
  OpenTelemetry.Api           StoreApi   — the only service
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
| `POST /stores` | Write path. Carries business attributes; gains the geocoding call in phase 6. |
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
  |-- CreateStore                                 child span, attrs store.id, store.name
  |     |-- GET nominatim.openstreetmap.org       client span; traceparent injected
  |     |-- save to dictionary
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
**Dependencies added:** `OpenTelemetry.Instrumentation.Http`.

`POST /stores` geocodes the submitted address by calling Nominatim, the
OpenStreetMap geocoding service, through `IHttpClientFactory`:

```
GET https://nominatim.openstreetmap.org/search?q={address}&format=json&limit=1
```

It is free and needs no key. It returns `403` to clients sending a generic
`User-Agent` — Postman's default is blocked — and permits one request per
second. Both are handled once, at client registration. An address it cannot
resolve returns an empty JSON array, which fails the request.

The answer to the phase question is that it is automatic:
`AddHttpClientInstrumentation()` creates the client span and injects the
`traceparent` header with no code on the calling side. The work in this phase is
proving that, then adding only the attributes the library cannot infer —
`url.full` redacts query-string values by default, so the address is set
deliberately as an application attribute or not at all.

`IHttpClientFactory` is used rather than a bare `HttpClient`. The tracing reason
is that it is the idiomatic registration point; the more important reason is that
it pools and recycles handlers, which is the standard mitigation for socket
exhaustion and stale DNS.

**A third party, not a second service.** An earlier draft of this spec built
`src/OpenTelemetry.GeocodingApi` for StoreApi to call. Calling a real service
instead is a deliberate trade, and it changes what the phase proves:

- **Proven:** the client span exists, sits under `CreateStore` in the waterfall,
  carries the trace ID, and leaves with a `traceparent` header StoreApi never
  wrote.
- **Not proven:** that anyone *reads* that header. Nominatim does not report to
  this account, so no server span appears beneath the client span. Continuation
  is asserted, not observed.
- **Gained:** real failure modes — `403` from a missing `User-Agent`, an empty
  result for an unresolvable address, real latency and real network variance —
  in place of a hardcoded dictionary and an artificial delay.

**One number, five causes.** The client span reports a single duration covering
time queued for a free connection, connection setup, transit each way, and
Nominatim's own processing. It cannot be decomposed from the client side. What
would separate them: the `System.Net.Http` metrics (`time_in_queue`,
`open_connections`, `connection.duration`) for pool health, the experimental
connection setup activity for setup cost, and `Server-Timing` for the remote
side, which Nominatim does not send. Metrics and the experimental connection
activities both stay out of scope; the limitation is recorded in the notes so
the number is never quoted as though it meant one thing.

**Verify:** a known address produces a trace whose waterfall is dominated by the
Nominatim call, and the stored `Store` carries coordinates. An unresolvable
address produces a failed trace with the error on the StoreApi spans. Sending the
same request through Postman with its default `User-Agent` — or removing the
configured one — produces a client span tagged `http.response.status_code: 403`.

**Note covers:** span kinds (server, client, internal); context propagation over
HTTP; that the injected header is the same standard typed by hand in phase 1;
why `url.full` redacts query values; what the client span's duration does and
does not include.

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

### Phase 8 — Deploy to Azure App Service

*Added 2026-10-21, after phases 1–7 were designed.*

**Question answered:** what does it take for this app to run in the cloud, and
does its trace still reach Better Stack from there?
**Dependencies added:** none. No change to `Program.cs` is expected — phase 7's
logging pipeline and `host.name` are already on `main`, and the code
already reads every value from configuration, and that is what makes it
deployable.

**Who does what.** This phase is different in kind: the owner performs every
Azure step by hand, from creating the resources to uploading the code and
setting app settings. Claude explains each step before it is taken and verifies
the result afterwards with read-only Azure CLI queries; Claude creates, changes
and deploys nothing in Azure. Creating the resources in the portal for the first
time is the point; scripting them is not part of this phase. Repository work —
the phase note, the `.http` variable, `.gitignore`, `CLAUDE.md`, the PR — is
Claude's, as in earlier phases.

**Account state at design time.** The Azure subscription was upgraded from the
free account to pay-as-you-go. A budget, `lab-budget`, is set at 1 USD a month
on the subscription, with actual and forecasted alerts at 100%. Everything in
this phase must cost nothing; any budget email means something outside the free
tier is running.

**The building blocks.** Three resources, each inside the last:

```
Resource group        a folder for everything this project creates
 └─ App Service plan  the compute — OS, region and pricing tier live here
     └─ Web App       StoreApi itself, at https://<name>.azurewebsites.net
```

- **Plan:** Linux, pricing tier **Free F1**. The tier belongs to the plan, not
  the app; that is why a $0 plan makes everything in it free.
- **Web App:** runtime stack **.NET 10 (LTS)** (`DOTNETCORE|10.0`, supported on
  Linux App Service until November 2028). Code deployment, not a container.
- **Region:** the owner's choice, near the owner. **Names:** builder's call; the
  Web App name must be globally unique because it becomes the hostname.

**Portal checks before pressing Create.** The Create Web App wizard pre-fills
defaults, and several of them would either cost money or leak a later phase:

| Tab | Must read | Why |
|---|---|---|
| Basics | Pricing plan **Free F1**, OS **Linux**, runtime **.NET 10** | Defaults are often a paid tier. Microsoft's own .NET quickstart uses `P0v3`, which is paid. |
| Deployment | Continuous deployment **Disabled** | That switch is phase 9. |
| Monitoring | Application Insights **No** | A second telemetry pipeline would compete with the one being taught, and it is a separate billable resource. |

**Getting the code there.** Built locally, uploaded by hand:

1. `dotnet publish -c Release -o publish` from `src/OpenTelemetry.Api`. The
   folder is opened and inspected before going further: it is exactly what
   Azure runs — `StoreApi.dll`, `appsettings.json`, the packages — and nothing
   else from the repository.
2. Zip the folder's contents, not the folder:
   `Compress-Archive -Path publish\* -DestinationPath app.zip`. A zip with a
   `publish/` directory at its root is not found by App Service.
3. `az webapp deploy --resource-group <rg> --name <app> --src-path app.zip --type zip`.
   App Service unpacks it into `/home/site/wwwroot` and restarts the app.

`publish/` and `app.zip` are build output and must never be committed;
`.gitignore` already covers `publish/` and gains `app.zip`.

**The deliberate first failure.** The first deploy is made *without* the Better
Stack token. `Program.cs` throws at startup when `BetterStack:SourceToken` is
missing, so the app will not start. This is kept, not avoided: the owner finds
the exception in the App Service log stream, which is the first thing to know
about any cloud app that will not start. Then:

- In the portal, **Environment variables → App settings**, add
  `BetterStack__SourceToken`. Double underscore, because Linux environment
  variable names cannot contain a colon; .NET maps `__` back to `:`, so the code
  reads the same key it reads locally from user secrets.
- Saving an app setting restarts the app. App settings override
  `appsettings.json` and are encrypted at rest — the cloud's equivalent of user
  secrets.
- `BetterStack:Endpoint` is not a secret and still comes from `appsettings.json`.

`ASPNETCORE_ENVIRONMENT` is deliberately left unset. The app then runs as
`Production`, and every exported span carries
`deployment.environment.name = Production` — the attribute that tells laptop
traces and cloud traces apart in the same Better Stack source.

**What the free tier changes.** F1 allows 60 CPU minutes a day; past that the
app returns `403` until midnight UTC. It has no Always On, so an idle app is
unloaded and the next request pays a cold start — visible in Better Stack as an
unusually long first request span. The in-memory store is emptied on every
restart and unload, so a store ID from yesterday can return `404`. All three are
expected behaviour to observe, not bugs to fix.

**Verify:**

1. Read-only CLI checks confirm the plan is Linux on `F1`, the Web App runs
   `DOTNETCORE|10.0` and is `Running`, and `lab-budget` still shows 0 spend.
2. `GET /stores` on the live URL returns `[]` with a `traceparent` header.
3. That trace ID finds the trace in Better Stack, under service `store-api`,
   with `deployment.environment.name = Production` and a `host.name` that is
   the App Service instance, not the laptop.
4. `GET /stores/{id}` with an unknown id on the live URL produces a trace whose
   `Store not found` warning appears beside it — phase 7's correlation, from
   the cloud.
5. **`client.address`, observed.** The request enricher tags each span with
   `RemoteIpAddress`. Behind App Service's load balancer that may be the
   balancer's address rather than the caller's, depending on whether forwarded
   headers are applied. The value the cloud traces actually carry is recorded,
   with no code change. The note also records the privacy trade-off: on a public
   URL this is third parties' IP addresses, a searchable attribute, sent to a
   third-party service — the convention lists it as Recommended, but keeping it
   is a decision, not a default.
6. `GET /stores/{id}/boom` on the live URL returns ProblemDetails and an error
   trace, as it does locally.
7. `POST /stores` with a known address produces a trace with the Nominatim
   client span. If Nominatim rejects or throttles requests from Azure's shared
   outbound addresses, that is recorded as a finding — the trace of the failure
   is itself the evidence, which is phase 6's lesson arriving unprompted.

The `.http` file gains a variable for the live host so the same requests run
against either target.

**End state.** The resources stay running after this phase. Phase 9 deploys to
the same Web App.

**Note covers:** resource group, plan and app, and why the tier lives on the
plan; the publish folder as the deployable unit, versus the repository; app
settings as configuration from outside the code, and the `__` rule; reading a
startup failure from the log stream; the environment and host resource
attributes; what a load balancer does to the caller's address, and the privacy
cost of recording it; free tier limits and cold starts.

**Out of scope for this phase:** CI/CD; creating resources from the CLI or as
infrastructure code; custom domains and certificates; Key Vault; Application
Insights; scaling; any database.

### Phase 9 — Automated deployment (CI/CD)

*Planned, not yet designed.* Direction agreed on 2026-10-21: a GitHub Actions
workflow deploys to the phase 8 Web App when a PR is merged into `main` — not
when it is approved, since approval does not put code on `main`. GitHub proves
its identity to Azure through OpenID Connect rather than a stored password. The
workflow automates exactly the publish, zip and deploy steps done by hand in
phase 8. This section is completed when the phase is designed.

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
