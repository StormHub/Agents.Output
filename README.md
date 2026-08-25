# Agents.Output

A .NET 10 API that hosts a Microsoft Agent Framework chat agent and exposes it over an AG-UI endpoint.

## Structure

- `src/Agents.Api`: ASP.NET Core API host exposing the chat agent over `/chat` (AG-UI) with a `CalendarDay` and `WeatherForecast` tool.
- `tests/Agents.Api.Evals`: agent evaluation suite — see its [README](tests/Agents.Api.Evals/README.md).
- `Agents.sln`: solution file referencing the projects under `src` and `tests`.

## Running

```bash
dotnet run --project src/Agents.Api
```

The agent expects an Ollama-compatible chat endpoint configured via `AgentChatOptions` (`Model`, `BaseUrl`) in `appsettings.json` or user secrets.

## Evaluating

```bash
dotnet test                      # offline, deterministic checks
EVAL_LIVE_MODEL=1 dotnet test    # also measures the agent against a live model
```

The live tier samples each check over many runs and judges it against a floor rather than
requiring every run to pass, and writes a JSON report per run. See the
[suite README](tests/Agents.Api.Evals/README.md) for how the floors and confidence bounds work.
