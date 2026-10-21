# CLAUDE.md

## Why this project exists

To learn how to use **OpenTelemetry** to collect the data that matters, rather
than collecting everything, and to send it to a monitoring tool, **Better
Stack**, where it can be searched.

It is a learning project, not a product. It is built in small phases, one idea
at a time, and each phase ends with a written note explaining what was learned.

It is aimed at three problems from real work:

1. **"Who sent this?"** A request fails, and nothing links it back to the caller.
2. **"Why only sometimes?"** A request fails for some inputs and not others, and
   the only way to find out is to ask the caller for a sample.
3. **"Where is store X?"** Business details such as a store ID can only be found
   by searching raw log text.

The full design lives in
`docs/superpowers/specs/2026-08-04-distributed-tracing-learning-project-design.md`.
That spec is the source of truth. Read it before proposing work.

### Words used in this file

| Term | Plain meaning |
|---|---|
| Trace | The whole story of one request, start to finish |
| Span | One step inside that story, with a start time and a duration |
| Attribute | A labelled fact attached to a span, such as `store.id` |
| `traceparent` | A standard HTTP header that carries a trace's ID from one service to the next |
| OpenTelemetry | The open standard and SDK that collect traces and send them on |
| Better Stack | The monitoring tool that receives and displays the traces |

## Where we are now

**Phases 1 to 7 are done.** A small store API records a trace for every
request. Each trace shows which store it involved, what went wrong if anything
failed, and the call made to an outside service (Nominatim, which turns an
address into map coordinates). Every trace is sent to Better Stack. Every
response hands the caller a `traceparent` header they can quote in a bug report.

When creating a store fails, the error is marked on the same span that holds the
store's name and address, so "failed requests for this address" is a single
search.

Log messages are also sent to Better Stack. They stay generic ("Store not
found"); the store ID lives on the trace, and each log line is linked to its
trace automatically. Every trace and log line also says which environment and
which machine it came from.

**Next:** phase 8, deploying the API to Azure App Service.

The technical detail of each phase is in its note under `docs/`. Update this
section in every phase's PR. Keep it short and in plain language.

## Roadmap

| # | Phase | What it teaches | Status |
|---|---|---|---|
| 1 | Activity, observed | What .NET already records, with zero packages | Done |
| 2 | Custom spans and attributes | Attaching `store.id` to an operation | Done |
| 3 | Exceptions and ProblemDetails | `IExceptionHandler`, errors on the span | Done |
| 4 | `traceparent` on the response | W3C Trace Context, header format | Done |
| 5 | OpenTelemetry SDK and Better Stack | Exporting via OTLP | Done |
| 6 | The network hop | Automatic client spans and `traceparent` injection | Done |
| 7 | Logs and trace correlation | Generic messages, structured properties | Done |

**Later, not yet designed:** a real database (SQL Server), deployment to Azure
App Service, and automated deployment (CI/CD). Each will be its own phase, added
to the spec only when it is designed.

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

## Check the official docs first

Look it up before implementing. Do not work from memory.

- **.NET concepts:** load the microsoft-docs plugin.
- **OpenTelemetry concepts:** use <https://opentelemetry.io/docs/languages/dotnet/>.
- **Always give the link** to the page used.

## The domain

Store onboarding. `Store { Id, Name, Address, Latitude, Longitude, CreatedAt }`.

Endpoints: `POST /stores`, `GET /stores/{id}`, `GET /stores`, and
`GET /stores/{id}/boom`, which throws deliberately. `PUT` and `DELETE` are
excluded on purpose — they teach nothing new about tracing.

## Tech stack

- .NET 10, ASP.NET Core minimal API, C# with nullable enabled.
- Storage: an in-memory `ConcurrentDictionary` singleton. No database.
- No test project. Verification is manual through
  `src/OpenTelemetry.Api/OpenTelemetry.Api.http`.
- OpenTelemetry SDK plus an OTLP exporter to Better Stack, from phase 5 onward.
- Secrets go in user secrets, never in `appsettings.json`.
- The project folder is named `OpenTelemetry.Api`, but `PackageId` and
  `AssemblyName` in the csproj are both `StoreApi`, to avoid clashing with the
  real `OpenTelemetry.Api` package.

## Layout

```
src/OpenTelemetry.Api            StoreApi   — the only service
docs/phase-N-<topic>.md          one note per phase; concepts live here
docs/superpowers/specs/          the design spec
```

## Conventions

Global conventions in `~/.claude/CLAUDE.md` apply in full. The ones that come up
most here:

- Feature branch per phase: `feature/phase-N-<topic>`. Never commit or push
  directly to `main`, which is protected by a ruleset.
- Conventional Commits.
- `dotnet build` must pass before any push.
- Merge commits only, never rebase.
- Every phase ends with a notes file and a PR that updates "Where we are now".
