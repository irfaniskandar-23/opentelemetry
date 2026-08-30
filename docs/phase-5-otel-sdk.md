# Phase 5 — the OpenTelemetry SDK and Better Stack

**Question answered:** what does the OpenTelemetry SDK actually add on top of the
four phases that installed nothing?
**Dependencies added:** three, the first in this project.

## What was built

- `OpenTelemetry.Api.csproj` — three packages, plus two lines that are collision
  fixes rather than renames (Trap 1).
- `Program.cs` — `AddOpenTelemetry()` with a resource, ASP.NET Core
  instrumentation, an enrichment hook and an OTLP exporter.
- `Program.cs` — the 55-line hand-written `ActivityListener` from phase 1,
  **deleted**.
- `appsettings.json` — the Better Stack endpoint, which is not a credential.
- User secrets — the source token, which is.

## The one idea in this phase

> The SDK is an `ActivityListener` with an industrial back end.

Phases 1–4 were not a warm-up for the real thing. They *were* the real thing,
with a `Console.WriteLine` where the exporter goes. Nothing about how spans are
produced changed in this phase — `Telemetry.Source.StartActivity("CreateStore")`
is the same line it was in phase 2, and ASP.NET Core has been emitting a
per-request `Activity` since phase 1 with zero packages installed.

The swap, line for line:

| Phase 1 listener | Phase 5 SDK |
|---|---|
| `ShouldListenTo = s => s.Name == …` | `.AddSource(Telemetry.SourceName)` |
| `Sample = … AllDataAndRecorded` | default sampler (`ParentBased(AlwaysOn)`) |
| `ActivityStopped = a => Console.WriteLine(…)` | `.AddOtlpExporter(…)` |
| *(nothing — ASP.NET Core spans were unnamed noise)* | `.AddAspNetCoreInstrumentation()` |

One listener was unsubscribed and another was subscribed in its place. That is
the whole migration. Everything below is detail hanging off it.

## Terminology, settled

Three words that are easy to blur, and everything downstream depends on them:

| Term | What it is |
|---|---|
| **Trace** | The whole request. Not an object — the *set* of spans sharing one trace id. |
| **Span** | One unit of work. The only real object: name, start, end, tags, kind. |
| **Root span** | The span in that set with **no parent**. Nothing else is special about it. |

A trace is not "the top row". It is the bag; the trace id is the label on the
bag. The top row is a span, and it happens to be the root because nothing above
it exists.

`POST /stores` observed, trace `e8f5f679…`:

```
Trace  e8f5f6795db6046306474058fc65c6f8      ← the bag, not a span
│
├── Span  8aeac67860364945  "POST /stores"   ← root:  parent_span_id absent
│         kind: server    scope: Microsoft.AspNetCore
│
    └── Span  4a4c570cb8738469  "CreateStore"
              kind: internal  scope: OpenTelemetry.Api 1.0.0
              parent_span_id: 8aeac67860364945
```

`scope` is the tell: it records which `ActivitySource` emitted the span, so it
maps straight back to either the instrumentation package or `Telemetry.cs`.

**Spans travel as a flat list.** There is no tree on the wire. Each span carries
a `parent_span_id` and the backend reassembles the hierarchy from those pointers
alone — the same mechanism the `traceparent` header uses across services in
phase 6, where the pointer is handed to a different process.

## Two halves of `Program.cs`

Everything before `builder.Build()` **describes**. Nothing runs. `AddOpenTelemetry()`
registers a `TracerProvider` in the DI container; the provider is constructed at
`Build()`, and the listener subscribes when the host starts. Everything after
`builder.Build()` is the request pipeline.

This matters because the failure modes differ: a mistake in the first half
produces a process that starts and exports nothing, and a mistake in the second
half produces a request that behaves oddly. They look nothing alike when
debugging.

## Resource — who is reporting

```csharp
.ConfigureResource(resource => resource
    .AddService("store-api")
    .AddAttributes([new("deployment.environment.name", builder.Environment.EnvironmentName)]))
```

A **resource attribute** describes the process. A **span attribute** describes
one operation. The resource is sent once per export batch and stamped onto every
span in it; `store.id` could never live there, and `service.name` would be
absurd repeated on every span.

Without `AddService`, spans arrive as `unknown_service:StoreApi` and cannot be
filtered by service — which stops being cosmetic the moment a second service
exists.

`deployment.environment.name` comes from `ASPNETCORE_ENVIRONMENT`: one
environment variable, set to `Development` by `launchSettings.json` locally, and
supplied as an application setting by a host like Azure App Service.
`launchSettings.json` is a development-only file and is never published, so on a
real host the variable is unset — and unset deliberately means **`Production`**.
That default is what keeps laptop traffic distinguishable from real traffic when
both export to the same source.

## What auto-instrumentation gives, and what it does not

`AddAspNetCoreInstrumentation()` subscribes to the request `Activity` ASP.NET
Core already emits and renames its data to the OpenTelemetry semantic
conventions — `http.request.method`, `url.path`, `http.route`,
`http.response.status_code`. Semantic conventions are why a backend can draw a
latency chart for a .NET service and a Python service on the same axes.

What it deliberately omits: request bodies, headers, cookies, auth. The
enrichment hook adds back what is safe:

```csharp
options.EnrichWithHttpRequest = (activity, request) =>
{
    activity.SetTag("http.request.body.size", request.ContentLength);
    activity.SetTag("http.request.header.content_type", request.ContentType);
    activity.SetTag("url.query", request.QueryString.Value);
    activity.SetTag("client.address", request.HttpContext.Connection.RemoteIpAddress?.ToString());
};
```

`client.address` reads `::1` locally — IPv6 loopback, the modern `127.0.0.1`.
Behind a load balancer this field returns the *proxy's* address unless
`UseForwardedHeaders` is configured, which is a production correctness issue
rather than a tracing one.

### Why the request body is not in that list

The instinct to log payloads for debugging is sound; putting them on spans is
not. Four reasons: spans are indexed and billed per byte, so bodies multiply cost
for data nobody reads; attribute values are truncated by the spec and by
backends, so long bodies arrive unusable; sampling means the one failing request
is exactly the one that may be dropped; and payloads carry PII into a third-party
system with its own retention, outside any deletion pipeline.

What goes on the span instead is **identity** — `store.id`, `tenant.id`,
`user.id`. Payload detail belongs on a log line emitted only on the failure path,
correlated by trace id. That is phase 7.

There is also a mechanical trap: `EnrichWithHttpRequest` runs *before* model
binding, and `request.Body` is a forward-only stream. Reading it there empties it,
`CreateStoreRequest` binds to null, and the endpoint fails in a way that looks
nothing like a tracing bug.

## Span kind

`kind` answers one question: what is this span's relationship to a **remote
boundary**?

| Kind | Meaning | Here |
|---|---|---|
| `server` | I received a remote request | `POST /stores` |
| `client` | I sent one and am waiting | none yet — phase 6 |
| `internal` | No boundary crossed | `CreateStore` |

`internal` is the default for `StartActivity`, which is why `CreateStore` got it
without asking. In practice `kind` is almost never set by hand: the spans that
need a non-default kind are the spans written by instrumentation packages, which
own the boundary.

The payoff arrives in phase 6. One network hop produces **two** spans in two
processes — a `client` span measured by the caller and a `server` span measured
by the callee. The difference between their durations is network plus queueing
time, a number neither service can compute alone. `kind` is what tells the
backend which is which. It is also how service maps are drawn: a `client` span
whose child is a `server` span in another service becomes an arrow.

Getting `kind` wrong breaks none of this loudly. The code runs, the span exports,
and an edge silently vanishes from the architecture diagram.

## When a manual span earns its place

`CreateStore` currently has no children — the "database" is a
`ConcurrentDictionary` write, which crosses no boundary and deserves no span.
It survives on its tags alone, and those tags are the founding problem of this
project: *which store did this request touch*.

The general rule, since over-producing spans is the common failure:

> Tags are cheap and always worth it. Spans cost money and screen space, so make
> them earn it. If a span has one child and adds no attributes, delete it.

To label a request, `Activity.Current?.SetTag(…)` writes to the ASP.NET Core span
directly — no child span needed. `StartActivity` is for operations worth timing
or grouping separately. In production such spans live in the domain layer rather
than the endpoint, because that is what makes an operation observable regardless
of whether an HTTP request, a queue message or a cron job triggered it.

## OTLP and the export path

```csharp
.AddOtlpExporter(options =>
{
    options.Endpoint = new Uri($"{betterStackEndpoint}/v1/traces");
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    options.Headers  = $"Authorization=Bearer {betterStackToken}";
});
```

OTLP is the wire format — protobuf messages, over gRPC or over HTTP. It is what
makes the backend replaceable: swapping Better Stack for Jaeger, Honeycomb or a
Collector changes these three lines and nothing else in the codebase.

Export is **batched and asynchronous**, on a background thread, flushing roughly
every five seconds. Spans do not appear the instant a request returns, and
`Ctrl+C` too soon can discard a batch. Neither is a fault.

### Secrets

The endpoint is not a credential and lives in `appsettings.json`. The token is
one and lives in user secrets:

```
dotnet user-secrets set "BetterStack:SourceToken" <token> --project src/OpenTelemetry.Api
```

`<UserSecretsId>` in the csproj is a **folder name**, not encryption. The file
sits at `%APPDATA%\Microsoft\UserSecrets\<guid>\secrets.json` in plain text, with
keys stored flat and colons literal:

```json
{ "BetterStack:SourceToken": "…" }
```

Its only guarantee is that it is outside the repository and cannot be committed.
Both values are read through `builder.Configuration`, so the code cannot tell
which layer answered — which is the point of the configuration abstraction.

## Trap 1 — the project shares a name with a real package

The single largest time sink of the phase, and it surfaced **twice from one
cause**. This project is called `OpenTelemetry.Api`. So is a real NuGet package
in the SDK's dependency tree.

**At restore:**

```
NU1108: Cycle detected.
```

NuGet matched the project's package identity against the dependency and
concluded the project depends on itself.

**At runtime, after fixing restore:**

```
System.TypeLoadException: Could not load type
'OpenTelemetry.Trace.TracerProviderBuilder' from assembly 'OpenTelemetry.Api'
```

The compiled output was `OpenTelemetry.Api.dll`, which shadowed the real library
assembly of the same name. The loader found the file, looked inside for the type,
and did not find it.

Two csproj lines fix both:

```xml
<PackageId>StoreApi</PackageId>     <!-- restore identity -->
<AssemblyName>StoreApi</AssemblyName> <!-- runtime identity -->
```

Neither renames the project. The folder, the namespaces and
`Telemetry.SourceName` are untouched, which is why the `scope` in exported spans
still reads `OpenTelemetry.Api`. The transferable lesson: **NuGet identity and
assembly identity are separate things**, and a name collision has to be fixed in
both places.

## Trap 2 — forgetting `AddSource`

`.AddSource(Telemetry.SourceName)` is the SDK's `ShouldListenTo`, and it is not
optional for exactly the reason it was not optional in phase 1: an
`ActivitySource` nobody subscribed to produces nothing, and `StartActivity`
returns `null`.

Omit the line and `POST /stores` spans keep arriving, because ASP.NET Core's
source was registered by `AddAspNetCoreInstrumentation()`. Only `CreateStore`
disappears. Half the trace, no error, no warning — the most confusing possible
state.

Instrumented code must never crash an uninstrumented process, which is why every
call in the endpoint is `activity?.SetTag(…)`.

## Trap 3 — the exporter's default protocol

The SDK defaults to OTLP over **gRPC**. Better Stack accepts **HTTP/protobuf**.
Leaving `options.Protocol` unset fails at the transport layer, in a background
thread, with no message resembling "wrong protocol".

## Trap 4 — a missing token fails silently

Both configuration reads throw rather than returning null:

```csharp
var betterStackToken = builder.Configuration["BetterStack:SourceToken"]
    ?? throw new InvalidOperationException("…Set it with dotnet user-secrets.");
```

A null token produces an `Authorization` header of `Bearer `, a 401 inside the
exporter's background thread, and no visible failure anywhere. The app looks
healthy and exports nothing. Failing at startup is strictly better than a service
that is silently unobservable.

## Verified

`POST /stores`, with the hand-written listener removed:

| Evidence | Value |
|---|---|
| Response header | `traceparent: 00-e8f5f679…-8aeac67860364945-01` |
| Better Stack, span 1 | `POST /stores` · root · `kind: server` · `scope: Microsoft.AspNetCore` |
| Better Stack, span 2 | `CreateStore` · `root: false` · `kind: internal` · `parent_span_id: 8aeac67860364945` |
| Resource on both | `service.name: store-api`, `deployment.environment.name: Development` |
| Business tags | `store.id`, `store.name` on `CreateStore` |

The header's span id matches the root span in the backend, and the trace id
matches both. Two spans, one trace, correctly nested, with no local listener in
the process.

## What this phase actually taught

That the four phases before it were not preparation. `Activity`, `ActivitySource`
and `ActivityListener` are the OpenTelemetry data model, already in the BCL under
older names, and installing the SDK replaces the *destination* rather than the
mechanism. A project that had started here would have working traces and no idea
what any of it meant.

The second lesson is where cost lives. Spans are indexed and billed, so the
discipline is fewer spans with better attributes — identity on the span, detail
on a correlated log, payloads in storage you control.

Phase 6 adds a second service. `CreateStore` gains its first child, a `client`
span pairs with a `server` span in another process, and the `traceparent` header
built by hand in phase 4 starts being read by something other than Postman.
