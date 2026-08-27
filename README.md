# Agents.Output

A .NET 10 API that hosts a Microsoft Agent Framework chat agent and exposes it over an AG-UI endpoint.

## Structure

- `src/Agents.Api`: ASP.NET Core API host exposing the chat agent over `/chat` (AG-UI) with a `CalendarDay` and `WeatherForecast` tool.
- `tests/Agents.Evals.Infrastructure`: fixtures and `EVAL_*` configuration shared by both evaluation suites — see its [README](tests/Agents.Evals.Infrastructure/README.md).
- `tests/Agents.Api.Evals`: agent evaluation suite — see its [README](tests/Agents.Api.Evals/README.md).
- `tests/Agents.Extensions.Evals`: `Microsoft.Extensions.AI.Evaluation` suite for the chat pipeline underneath the agent — see its [README](tests/Agents.Extensions.Evals/README.md).
- `Agents.sln`: solution file referencing the projects under `src` and `tests`.

## Running

```bash
dotnet run --project src/Agents.Api
```

The agent expects an Ollama-compatible chat endpoint configured via `AgentChatOptions` (`Model`, `BaseUrl`) in `appsettings.json` or user secrets.

## Evaluating

Two suites measure two layers of the same system, and neither subsumes the other.

```bash
dotnet run --project tests/Agents.Api.Evals             # does the agent route to the right tool?
dotnet run --project tests/Agents.Extensions.Evals      # is the answer relevant, grounded, correct?
```

Both skip their live tiers unless `EVAL_LIVE_MODEL=1` is set, so an unconfigured run is offline,
deterministic and free. They share their scenarios, scripted client, canned tools and `EVAL_*`
configuration through `tests/Agents.Evals.Infrastructure` — including one User Secrets store, so
`dotnet user-secrets set EVAL_API_KEY "..." --project tests/Agents.Evals.Infrastructure` configures
the live tiers of both. What each suite keeps to itself is what it gates on.

`Agents.Api.Evals` samples each check over many runs and judges it against a floor rather than
requiring every run to pass — see its [README](tests/Agents.Api.Evals/README.md) for how the floors
and confidence bounds work. `Agents.Extensions.Evals` scores the chat pipeline with the
`Microsoft.Extensions.AI.Evaluation` libraries and writes to a result store that
`dotnet aieval report` renders — see its [README](tests/Agents.Extensions.Evals/README.md).
