# Agents.Output

A .NET 10 API that hosts a Microsoft Agent Framework chat agent and exposes it over an AG-UI endpoint.

## Structure

- `src/Agents.Api`: ASP.NET Core API host exposing the chat agent over `/chat` (AG-UI) with a `CalendarDay` and `WeatherForecast` tool.
- `src/Agents.ConsoleApp`: interactive AG-UI client that streams a conversation against a running `Agents.Api` at `http://localhost:5000/chat`.
- `tests/Agents.Evals`: both evaluation suites — `Trajectory/` measures the Agent Framework agent (does it route to the right tool?), `Metrics/` measures the chat pipeline underneath it with the `Microsoft.Extensions.AI.Evaluation` libraries (is the answer relevant, grounded, correct?).
- `tests/Agents.Evals.Infrastructure`: the fixture that composes the live system, strongly-typed `EvaluationOptions` configuration, and the rate/gate arithmetic both suites share.
- `tests/Agents.Evals.Infrastructure.Tests`: tests for that arithmetic — the one part of the evaluation stack that is deterministic enough to assert on.
- `Agents.sln`: solution file referencing the projects under `src` and `tests`.

## Running

```bash
dotnet run --project src/Agents.Api
```

The agent expects an Azure OpenAI chat endpoint configured via `AgentChatOptions` (`Model`, `BaseUrl`, `ApiKey`) in `appsettings.json` or user secrets.

## Evaluating

Two suites measure two layers of the same system, and neither subsumes the other. They live side by
side in one project.

```bash
dotnet test tests/Agents.Evals                       # does the agent route, and is the answer good?
dotnet test tests/Agents.Evals.Infrastructure.Tests  # is the gate arithmetic itself correct?
```

Both suites skip their live tiers unless `EvaluationOptions__LiveModelEnabled=true` is set, so an
unconfigured run is offline, deterministic and free. They share their scenarios, scripted client,
canned tools, `EvaluationOptions` configuration and rate/gate arithmetic through
`tests/Agents.Evals.Infrastructure` — including one User Secrets store, so
`dotnet user-secrets set EvaluationOptions:ApiKey "..." --project tests/Agents.Evals.Infrastructure`
configures the live tiers of both.

What each suite keeps to itself is the policy: which checks exist and what floor each has to clear.
The shared project knows how to judge a rate against a floor; it does not know a single floor.

`Trajectory/` samples each check over many runs and judges it against a floor rather than requiring
every run to pass, because the agent is stochastic. `Metrics/` scores the chat pipeline with the
`Microsoft.Extensions.AI.Evaluation` libraries and writes to a result store that
`dotnet aieval report` renders.
