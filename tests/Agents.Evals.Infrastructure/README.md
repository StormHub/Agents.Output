# Agents.Evals.Infrastructure

The fixtures and the environment both evaluation suites share.

| | Lives here | Stays in the suite |
|---|---|---|
| What the agent is | `AgentContract` — name, instructions, tool names | — |
| What is asked | `WeatherScenarios`, `WeatherScenario` | queries with no scripted counterpart |
| What the model says offline | `ScriptedChatClient` | — |
| What the tools return | `StubWeatherTools`, `ToolResults` | — |
| Where it points | `EvalEnvironment`, `EvalServices` | — |
| How a sample becomes a verdict | `Probabilistic/` — rates, Wilson bounds, floor comparison, the report | — |
| **Which checks exist, and what floor each gets** | — | **every check, floor, evaluator and assertion** |

That last row is the design rule: **share the mechanism, keep the policy.**

`EvalGate.Evaluate(rates, floors)` knows how to judge a measured rate against a floor. It does not
know a single floor — every one is passed in, and they are declared in
`LiveModelEvalTests.Floors` alongside the reasoning for each. Same for `EvalReport`: it renders
whatever it is handed. If both suites agreed on what "good" means they would stop being two
measurements of the same system and start being one measurement counted twice.

## This project is also a test suite

`Probabilistic/` decides whether a live run goes red. Gating on arithmetic nobody checked is how a
suite ends up quietly asserting nothing, so `EvalGateTests` lives beside it rather than in whichever
suite happens to call it:

```bash
dotnet run --project tests/Agents.Evals.Infrastructure   # 12 tests, no model, no network
```

These are the strictest tests in the repository, and the only ones that are genuinely
deterministic: the statistics are fixed functions of the counts. That is why the project carries an
xunit reference despite being a library the other two reference — it is both.

The cost of that is small but real: xunit's build targets flow transitively, so anything
referencing this project inherits an auto-generated entry point. Both suites are xunit projects
already, so it costs them nothing — but a plain library or console app referencing this would get a
`CS7022` warning about a duplicate entry point. If that ever becomes a problem, split
`Probabilistic/` out rather than moving its tests away from it.

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

`Probabilistic/` arrived later, from `Agents.Api.Evals`. It was never agent-specific: `CheckRate`
and `EvalGate` are arithmetic over counts, and `EvalRates`/`EvalReport` only touch the Agent
Framework to read a batch of results. Rate gating is the right answer to a stochastic system at any
layer, so it belongs where the second suite can reach it.

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
| `EVAL_REPORT_DIR` | `eval-reports/` beside the test binary | `Agents.Api.Evals` |
| `EVAL_REPORT_FORMAT` | `all` (`gate-summary`, `json`, `html`, comma-separated) | `Agents.Api.Evals` |

Every one of them is now defined here — there is no second place to look. The defaults for
`EVAL_MODEL` and `EVAL_BASEURL` match `Agents.Api`'s own `appsettings.json`, so an unconfigured run
measures the deployment the API actually uses.

The "read by" column records who happens to consume each knob today, not a restriction. A knob only
one suite reads is still defined once and layered the same way, so it cannot come to mean two
things the way `EVAL_BASEURL` did.

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
