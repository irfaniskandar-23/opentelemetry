# Phase 8 — Deploy to Azure App Service

**Question answered:** what does it take for this app to run in the cloud, and
does its trace still reach Better Stack from there?

**Status:** done. No package added and no change to `Program.cs`. The owner
created and deployed everything in Azure by hand; Claude checked the result with
read-only queries (Azure CLI and Better Stack) on 2026-11-05.

---

## What exists now

| Resource | Name | Setting |
|---|---|---|
| Resource group | `otel-lab` | Southeast Asia |
| App Service plan | `asp-otel-lab` | Linux, **Free F1** |
| Web App | `otel-store-api` | `DOTNETCORE\|10.0`, Running |

Live URL: `https://otel-store-api-b9f9gffrcmh5a0gk.southeastasia-01.azurewebsites.net`

The hostname is not `<name>.azurewebsites.net`, as the design assumed. It carries
a random suffix and the region.

| File | Change |
|---|---|
| `OpenTelemetry.Api.http` | `@LocalHost` and `@LiveHost` variables; one line chooses which target every request goes to. |
| `.gitignore` | `app.zip`, the deployment zip, is build output. |

---

## The tier lives on the plan

A resource group is a folder. The **plan** is the compute: operating system,
region and pricing tier. The **Web App** runs on the plan. Because the tier
belongs to the plan, a Free plan makes every app inside it free.

```
otel-lab              resource group
 └─ asp-otel-lab      plan: Linux, F1
     └─ otel-store-api   the app
```

---

## The deployable unit is the publish folder

Azure does not receive the repository. It receives the output of
`dotnet publish`: `StoreApi.dll`, `appsettings.json` and the packages. The
contents of that folder (not the folder itself) are zipped and uploaded, and App
Service unpacks them into `/home/site/wwwroot`.

---

## Configuration from outside the code

The Better Stack token never leaves user secrets on the laptop. In Azure it is
an **app setting**, `BetterStack__SourceToken`. App settings reach the app as
environment variables, override `appsettings.json`, and are encrypted at rest.

The double underscore is the rule to remember. Linux environment variable names
cannot contain `:`, so `BetterStack:SourceToken` becomes
`BetterStack__SourceToken`, and .NET turns `__` back into `:` when it reads
configuration. The code reads the same key in both places and never knows which
source answered.

The app settings on the Web App are `BetterStack__SourceToken` and
`WEBSITE_HTTPLOGGING_RETENTION_DAYS`. The second is App Service's own, not the
app's. `ASPNETCORE_ENVIRONMENT` is deliberately not set, so the app runs as
`Production`.

**Reading a startup failure.** The first deploy was made without the token on
purpose. `Program.cs` throws at startup when `BetterStack:SourceToken` is
missing, so the app does not start, and no trace can reach Better Stack because
the exporter never runs. The only place that failure is visible is the App
Service **log stream**. That is the first place to look for any cloud app that
will not start.

---

## What the cloud traces show

All observed in Better Stack, source `otel-learning`, on 4 and 5 November.

| Attribute | Value | What it means |
|---|---|---|
| `deployment.environment.name` | `Production` | Laptop traces say `Development`. This is what tells them apart in one source. |
| `host.name` | `7ce991914370` (4 Nov), `7a3f98bab9b6` (5 Nov) | The App Service container, not the laptop. It changed overnight: the app came back on a new container. |
| `server.address` | the Azure hostname | |
| `url.scheme` | `http` | The public URL is `https`. HTTPS ends at App Service's front end; the app behind it receives plain HTTP. |
| `client.address` | `::ffff:169.254.129.1` | See below. |

### `client.address`: the front end, not the caller

`169.254.x.x` is a link-local address: App Service's front end, which passes
each request to the app. Forwarded headers are not applied, so
`RemoteIpAddress`, and therefore `client.address`, is the front end. Every cloud
request carries the same value.

Today that makes the attribute harmless but useless. If forwarded headers were
ever applied, it would start recording **third parties' IP addresses**, as a
searchable attribute, sent to a third-party service. The convention lists
`client.address` as Recommended, but keeping it then would be a decision, not a
default.

---

## What the free tier changes

- **CPU quota.** F1 allows 60 CPU minutes a day. Past that, every request gets
  `403` until midnight UTC.
- **No Always On.** An idle app is unloaded and started again by the next
  request.
- **Memory is the database.** The in-memory store empties on every restart.
  A store created on 4 November was gone on 5 November: `GET /stores` returned
  `[]`.

**Cold starts did not show up in a span.** The design expected a long first
request span after a restart. On 4 November the app logged its startup at
13:18:03 UTC, and the first request span, at 13:18:05, took 7 ms. A request span
starts only once ASP.NET Core is running, so time spent starting the process
falls outside every span.

---

## Verify

| # | Check | Result |
|---|---|---|
| 1 | Plan Linux on `F1`, app `DOTNETCORE\|10.0` and Running, budget at 0 | Plan and app ✅. Budget **not 0**; see the open item below. |
| 2 | `GET /stores` on the live URL returns `[]` with a `traceparent` header | ✅ |
| 3 | That trace is in Better Stack under `store-api`, `Production`, an Azure `host.name` | ✅ |
| 4 | Unknown id on the live URL → `404`, with `Store not found` attached to the same trace | ✅ trace `e113a38a8dfbea72f77f3728907e5660`, `store.id` on the span |
| 5 | Record the `client.address` cloud traces carry | ✅ `::ffff:169.254.129.1` |
| 6 | `/boom` on the live URL → ProblemDetails and an error trace | ✅ |
| 7 | `POST /stores` reaches Nominatim from Azure | ✅ `201`; Azure's shared addresses were not blocked |

### Open item: the budget is not at zero

`lab-budget` (US$1 a month) showed **US$0.0875** spent in November when read on
5 November. The subscription holds only the plan and the app. The CLI's usage
report returns no cost per line for this subscription, so the source is not yet
known. By the design's rule, any spend means something outside the free tier is
running, so this needs finding before the bill grows.

---

## Concepts this phase answers

- Resource group, plan and app, and why the pricing tier lives on the plan.
- The publish folder, not the repository, is what gets deployed.
- App settings are configuration from outside the code, and `:` becomes `__` on
  Linux.
- A startup failure produces no trace; the log stream is where it shows.
- Environment and host resource attributes keep laptop and cloud traces apart.
- A front end in the path changes what "the caller's address" means, and
  recording a real caller's address has a privacy cost.
- Free tier limits, and why a cold start is invisible to a request span.

## Sources

- [What are Azure App Service plans?](https://learn.microsoft.com/azure/app-service/overview-hosting-plans)
- [Configure an App Service app: app settings](https://learn.microsoft.com/azure/app-service/configure-common#configure-app-settings)
- [Configure an ASP.NET Core app for App Service: environment variables](https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore#access-environment-variables)
- [App Service quotas and metrics](https://learn.microsoft.com/azure/app-service/web-sites-monitor)
- [App Service limits](https://learn.microsoft.com/azure/azure-resource-manager/management/azure-subscription-service-limits#azure-app-service-limits)
- [Use .http files: variables](https://learn.microsoft.com/aspnet/core/test/http-files?view=aspnetcore-10.0)
