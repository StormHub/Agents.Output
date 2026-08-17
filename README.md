# Agents.Output

A .NET 10 API that hosts a Microsoft Agent Framework chat agent and exposes it over an AG-UI endpoint.

Migrated from [StormHub/Agents.Resources](https://github.com/StormHub/Agents.Resources/tree/main/weather/api/Agents.Api), upgraded to the latest Microsoft Agent Framework packages.

## Structure

- `src/Agents.Api`: ASP.NET Core API host exposing the chat agent over `/chat` (AG-UI) with a `CalendarDay` and `WeatherForecast` tool.
- `Agents.sln`: solution file referencing the projects under `src`.

## Running

```bash
dotnet run --project src/Agents.Api
```

The agent expects an Ollama-compatible chat endpoint configured via `AgentChatOptions` (`Model`, `BaseUrl`) in `appsettings.json` or user secrets.
