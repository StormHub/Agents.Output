# Agents.Evals.Infrastructure

The fixtures and the environment both evaluation suites share.

| | Lives here | Stays in the suite |
|---|---|---|
| What the agent is | `AgentContract` — name, instructions, tool names | — |
| What is asked | `WeatherScenarios`, `WeatherScenario` | queries with no scripted counterpart |
| What the model says offline | `ScriptedChatClient` | — |
| What the tools return | `StubWeatherTools`, `ToolResults` | — |
| Where it points | `EvalEnvironment`, `EvalServices` | knobs only one suite has |
| **What counts as passing** | — | **every check, floor, evaluator and assertion** |

That last row is the whole design rule: **sharing a fixture is safe, sharing a verdict is not.**
If both suites agreed on what "good" means they would stop being two measurements of the same
system and start being one measurement counted twice. So this project has no test framework
reference and no evaluation library reference — it cannot express a verdict even by accident.

## Why it exists

`Agents.Api.Evals` measures the `ChatClientAgent` the API hosts; `Agents.Extensions.Evals` measures
the `IChatClient` pipeline underneath it. Different libraries, different questions — but the same
agent, the same tool contract, the same deployment and the same credential.

Before this project each suite carried its own copy of the scripted client, the canned tools, the
scenario record and the `EVAL_*` layering. Four files were duplicates modulo comments, and the two
copies had already drifted: `EVAL_BASEURL` defaulted to a local Ollama endpoint in one suite and to
the Azure endpoint in the other, so the same variable meant two different things depending on which
suite you ran. That is the failure mode this project exists to prevent — a copy that drifts makes a
suite measure something the API never does.

## Configuration

Every knob resolves through one layered `IConfiguration`: in-memory defaults → User Secrets →
environment variables. The secrets store belongs to *this* assembly, so one command configures both
suites:

```bash
dotnet user-secrets set EVAL_API_KEY "..." --project tests/Agents.Evals.Infrastructure
```

| Variable | Default | Read by |
|---|---|---|
| `EVAL_LIVE_MODEL` | unset (live tiers skipped) | both |
| `EVAL_MODEL` | `gpt-4.1-dz-1` | both |
| `EVAL_BASEURL` | `https://shared-openai.openai.azure.com` | both |
| `EVAL_API_KEY` | unset — required by the live tiers | both |
| `EVAL_SAMPLE_SIZE` | `35` | `Agents.Api.Evals` |
| `EVAL_JUDGE_MODEL` | `EVAL_MODEL` | `Agents.Extensions.Evals` |
| `EVAL_QUALITY_FLOOR` | `3.0` out of 5 | `Agents.Extensions.Evals` |
| `EVAL_SAFETY_ENDPOINT` | unset (safety tier skipped) | `Agents.Extensions.Evals` |
| `EVAL_STORE_DIR` | `eval-store/` beside the test binary | `Agents.Extensions.Evals` |
| `EVAL_EXECUTION_NAME` | `local-<timestamp>` | `Agents.Extensions.Evals` |

The defaults for `EVAL_MODEL` and `EVAL_BASEURL` match `Agents.Api`'s own `appsettings.json`, so an
unconfigured run measures the deployment the API actually uses.

A knob only one suite understands — `EVAL_REPORT_DIR` and `EVAL_REPORT_FORMAT` mean nothing outside
`Agents.Api.Evals`' report writer — stays defined in the suite that owns it, but still reads through
`EvalEnvironment.Setting(key)`. So "environment variable beats User Secrets beats default" holds for
every knob, not just the shared ones.

## Reaching into `Agents.Api`

`AgentContract` is the only place that touches `Agents.Api`'s internals, so the suites do not have
to. Every name it exposes is derived from the production symbol rather than typed out:

```csharp
public const string WeatherToolName = nameof(WeatherForecast.GetWeatherForecast);
```

A rename in `Agents.Api` therefore breaks this build, instead of silently turning a tool-call check
into one that can never fire.

`EvalServices.ForLiveModel(model, withProductionTools)` builds the live client through production's
own `AddWeatherChatAgent`, so both suites measure the client the API builds rather than a lookalike.
It returns the provider rather than a client because the suites want different things out of it —
`Agents.Api.Evals` resolves the `ChatClientAgent`, `Agents.Extensions.Evals` resolves the keyed
`IChatClient` underneath it. `withProductionTools: false` (the default) leaves the tool collection
empty so the caller can supply `StubWeatherTools` and keep the readings fixed and knowable.

## Adding a scenario

Add a `WeatherScenario` to `WeatherScenarios`: name, query, ordered tool calls, scripted answer, and
reference answers. Keep the scripted answer as the first reference unless you mean to measure the
wording itself, and make the numbers in it numbers `StubWeatherTools` actually returns — otherwise
the grounding checks will fail the scenario correctly and confusingly.

A scenario added here shows up in **both** suites, which is the point: the same case is then
measured at both layers.

The scripted client is stateless — it decides what to emit by inspecting the conversation rather
than counting calls — so repeated and concurrent runs cannot interleave. Each tool may appear at
most once per scenario.
