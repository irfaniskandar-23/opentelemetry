# OpenTelemetry

.NET 10 minimal Web API project template.

## Run

```
dotnet run --project src/OpenTelemetry.Api
```

## Claude Code

The project turns on the [Better Stack plugin](https://github.com/BetterStackHQ/claude-plugin)
(incidents, logs, traces) in `.claude/settings.json`, but Claude Code does not
install it for you. After cloning, open Claude Code in the repo, accept the
folder trust prompt, then install it once:

```
claude plugin install betterstack@betterstack --scope project
```

The first time a Better Stack tool runs, sign in to Better Stack in the browser.
You need to be a member of the project's Better Stack team.

Skip the install if `claude plugin list` already shows `betterstack@synced`,
which means the plugin comes from your claude.ai account; installing it as well
would give you two copies.
