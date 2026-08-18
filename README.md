# Agents.Output

A .NET 10 API that hosts a Microsoft Agent Framework chat agent and exposes it over an AG-UI endpoint.

Migrated from [StormHub/Agents.Resources](https://github.com/StormHub/Agents.Resources/tree/main/weather/api/Agents.Api), upgraded to the latest Microsoft Agent Framework packages.

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
requiring every run to pass. Results are stored in `Microsoft.Extensions.AI.Evaluation.Reporting`
format, so runs accumulate a history:

```bash
dotnet tool restore
dotnet aieval report --output eval-report.html
```

See the [suite README](tests/Agents.Api.Evals/README.md) for how the floors, confidence bounds
and result store fit together.
