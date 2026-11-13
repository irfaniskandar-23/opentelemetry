# Phase 9 — Noticing failures

**Question answered:** when a request fails in Production, how does a person
find out, and how do they get from the alert to the failing code?

**Status:** done on 2026-11-13, except the owner's hand-written post-mortem
(see Verify). Two small code changes plus Better Stack configuration. The owner
deployed the code to Azure in one release; Claude built the zip and checked the
result with Better Stack queries. It answers the first box of GitHub issue #13.

---

## What exists now

```
failed request span -> chart of failed Production requests -> alert
                    -> incident -> email
```

A failure in Production now opens one incident per kind of failure and endpoint,
emails the owner, and links to exactly those failed requests. A failure on a
laptop opens nothing.

### Code changes

| File | Change |
|---|---|
| `GlobalExceptionHandler.cs` | Marks the request span `Error` and sets `error.type`; logs once; always returns `true`; the response status fills the body's `status` |
| `ObservabilityExtensions.cs` | `IncludeFormattedMessage = true`, so the log list shows the filled-in message |
| `StoresController.cs` | `/boom` throws a `NullReferenceException` and carries `store.id` |
| `.claude/rules/comments.md` | The 20-word limit applies per comment block |

The handler's log reads `Request failed: NullReferenceException`. It replaces
phase 3's `AddException`: .NET 10 no longer logs an exception that a handler
reports as handled, and OpenTelemetry now records exceptions as log records.

### Better Stack configuration (outside Git)

Everything below lives in Better Stack, not in the repository. It is written
here so it can be recreated.

**Two labels** on source `otel-learning`, type `string_low_cardinality`, new data
only:

| Label | Expression |
|---|---|
| `environment` | `JSONExtract(raw, 'resource', 'attributes', 'deployment.environment.name', 'Nullable(String)')` |
| `error_type` | `JSONExtract(raw, 'span', 'attributes', 'error.type', 'Nullable(String)')` |

**Chart** "Production failures by error type and endpoint" on dashboard
"Tracing", section "Failures":

```sql
SELECT
  {{time}} AS time,
  concat(label('error_type'), ' on ', label('span_name')) AS series,
  label('error_type') AS error_type,
  label('http_target') AS route,
  sum(spans_count) AS value
FROM {{source}}
WHERE dt BETWEEN {{start_time}} AND {{end_time}}
  AND label('error') = '1'
  AND label('kind') = 'server'
  AND label('environment') = 'Production'
GROUP BY time, series, error_type, route
```

The extra columns `error_type` and `route` exist so the alert can use them as
`{{error_type}}` and `{{route}}`.

**Alert** "Store API Failure":

| Setting | Value |
|---|---|
| Rule | any series `> 0`, every minute, on the last minute, no confirmation wait |
| Incident per series | on |
| Recovery period | Never, so incidents stay open until someone resolves them |
| Notifications | email only |
| Incident name | `Production failure: {{series_name}}` |
| Incident cause | what failed (`{{error_type}}`), the endpoint (`{{route}}`), the latest check's count, and a link to the failed requests |

The link in the cause is an Explore traces URL that filters on four things:
`span.status.code="error"`, the Production environment,
`span.attributes.error.type="{{error_type}}"` and
`span.attributes.http.route="{{route}}"`.

`Send-Boom.cmd`, kept outside the repository, sends 100 failing requests to the
live app after asking for confirmation. It is a manual aid, not part of the app.

---

## What the real incidents showed

**Better Stack's `error` label is only `span.status.code = 'error'`.** It does
not look at the HTTP status or `error.type`. A 4xx leaves the status unset, so it
never opens an incident; two 404s in Production confirmed it. A 5xx only counts
because the handler sets the status.

**An incident is a question, not a counter.** "Failures in the latest check" is
the latest check's value: it went 76, then 5. The real count is on the chart or
in the traces.

**One incident per series, and why the series is type plus endpoint.** Type only
would be worse: with incidents that stay open, a second, unrelated bug with the
same type on another endpoint would join the open incident and send no email. A
`POST /stores` failure (`InvalidOperationException`) opened its own incident and
its own email, beside the `/boom` one.

**A failure that joins an open incident sends no new email.** Only the first one
notifies.

**The first email arrives about two and a half minutes after the failure**: a
one-minute check on a one-minute window, plus the push delay.

**"2 similar incidents" is Better Stack's incident grouping**, a team setting
that groups incidents starting within a timeframe. They stay separate incidents.

**The `root` filter was dropped.** The old chart counted only root spans. If a
caller sends a `traceparent`, our server span has a parent, so it is probably not
"root" and its failures would go uncounted. Server spans are one per request, so
`kind = 'server'` is enough. This was reasoned, not tested.

**The free plan limits the alert.** Push, SMS, calls and on-call schedules need a
paid plan, so the only channel is email.

---

## Verify

| # | Check | Result |
|---|---|---|
| 1 | Laptop `/boom`: `error.type`, `store.id`, one Error log linked to the trace, no incident | ✅ |
| 2 | Live `GET /stores` answers and its trace is `Production` from an Azure host | ✅ |
| 3 | Live `/boom` opens one incident named with type and endpoint, by email | ✅ 100 requests, one incident, email 2 min 20 s later |
| 4 | More `/boom` requests join the open incident instead of opening another | ✅ the value went 76 to 5 |
| 5 | A failure of another type and endpoint opens its own incident | ✅ |
| 6 | Each incident's link lists only its own failed requests | ✅ `POST stores`: one row; `/boom`: only `/boom` rows |
| 7 | The stack trace names the failing line | ✅ for `POST /stores`: `Create ... line 55`. ⚠️ Not for `/boom`, see below |
| 8 | A 4xx opens no incident | ✅ |
| 9 | The owner writes a post-mortem by hand | Pending |

The design planned two deploys, `main` alone and then phase 9. They were made as
one release, so a breakage could not be traced to a single change. Nothing broke.

---

## Open items

- **`/boom` loses its stack frame in Production.** In a Release build the
  optimizer inlines the three-line method into the generated call stub, so the
  trace starts at `lambda_method4`. Real endpoints keep their frame, as
  `POST /stores` shows. Accepted as minor. Adding
  `[MethodImpl(MethodImplOptions.NoInlining)]` to `Boom` would restore it.
- **Framework 4xx exceptions** such as `BadHttpRequestException` (body too
  large, body too slow) would become a 500 and open an incident. Not reproduced;
  waits for a real case.
- **"No coordinates for this address"** is still a 500. Left as it is.
- **The "Failures" dashboard section sits at the bottom**, below nine charts. It
  can be dragged to the top in the dashboard.
- **The link depends on `http.route`.** Renaming an endpoint's route changes the
  filter, and the old incidents' links stop matching.
- **Phase 8's budget item** (US$0.0875 spent) is still open.

---

## Concepts this phase answers

- Alerts count and traces explain: why the alert reads metrics, not spans.
- Metric labels versus raw attributes, and why a label must have few values.
- A span's status is the failure flag; `error.type` says which kind.
- Why .NET 10 needs the handler to log, and why it always returns `true`.
- Exceptions as log records, and the attributes the SDK adds from the exception.
- One incident per series, manual resolve, and what a joining failure does.
- How Release builds change a stack trace (inlining).
- What the free plan leaves out.

## Sources

- [Exceptions in logs: semantic conventions](https://opentelemetry.io/docs/specs/semconv/exceptions/exceptions-logs/)
- [HTTP spans: semantic conventions](https://opentelemetry.io/docs/specs/semconv/http/http-spans/)
- [Error attributes: `error.type`](https://opentelemetry.io/docs/specs/semconv/registry/attributes/error/)
- [Handle errors in ASP.NET Core: `IExceptionHandler`](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0#iexceptionhandler)
- [.NET 10 breaking change: exception diagnostics suppressed](https://learn.microsoft.com/aspnet/core/breaking-changes/10/exception-handler-diagnostics-suppressed?view=aspnetcore-10.0)
- [`MethodImplOptions`](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.methodimploptions)
- [Better Stack: incident grouping](https://betterstack.com/docs/uptime/incident-grouping/)
