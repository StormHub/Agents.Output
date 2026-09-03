# Agents.Evals.Infrastructure

The fixtures and the environment both evaluation suites share.

| | Lives here | Stays in the suite |
|---|---|---|
| What the agent is | `AgentContract` — name, instructions, tool names | — |
| What is asked | `WeatherScenarios`, `WeatherScenario` | queries with no scripted counterpart |
| What the model says offline | `ScriptedChatClient` | — |
| What the tools return | `StubWeatherTools`, `ToolResults` | — |
| Where it points | `EvalOptions`, `EvalEnvironment`, `EvalServices` | — |
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
suite ends up quietly asserting nothing, so `EvalGateTests` lives in `Agents.Evals.Infrastructure.Tests`
rather than in whichever suite happens to call it:

```bash
dotnet run --project tests/Agents.Evals.Infrastructure.Tests   # gate arithmetic, no model, no network
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
scenario record and the configuration layering. Four files were duplicates modulo comments, and the
two copies had already drifted: the base URL defaulted to a local Ollama endpoint in one suite and
to the Azure endpoint in the other, so the same variable meant two different things depending on
which suite you ran. That is the failure mode this project exists to prevent — a copy that drifts makes a
suite measure something the API never does.

`Probabilistic/` arrived later, from `Agents.Api.Evals`. It was never agent-specific: `CheckRate`
and `EvalGate` are arithmetic over counts, and `EvalRates`/`EvalReport` only touch the Agent
Framework to read a batch of results. Rate gating is the right answer to a stochastic system at any
layer, so it belongs where the second suite can reach it.

## Configuration

Every knob is a property on `EvalOptions`, bound once from a layered `IConfiguration`: an in-memory
defaults table → User Secrets → environment variables. The environment is a *source* of these
values, not their definition — a knob has one name, one type and one default whichever way it
arrives, and `EvalEnvironment.Current` is the bound, validated result the suites read:

```csharp
var floor = EvalEnvironment.Current.QualityFloor;   // double, not a parsed string
```

Values bind from the `Eval` section, so the standard spellings apply: `Eval__SampleSize` as an
environment variable, `Eval:SampleSize` in User Secrets. The secrets store belongs to *this*
assembly, so one command configures both suites:

```bash
dotnet user-secrets set Eval:ApiKey "..." --project tests/Agents.Evals.Infrastructure
```

| Property | Environment variable | Default | Read by |
|---|---|---|---|
| `LiveModelEnabled` | `Eval__LiveModelEnabled` | `false` (live tiers skipped) | both |
| `Model` | `Eval__Model` | `gpt-4.1-dz-1` | both |
| `BaseUrl` | `Eval__BaseUrl` | `https://shared-openai.openai.azure.com` | both |
| `ApiKey` | `Eval__ApiKey` | unset — required by the live tiers | both |
| `SampleSize` | `Eval__SampleSize` | `35` | `Agents.Api.Evals` |
| `JudgeModel` | `Eval__JudgeModel` | follows `Model` | `Agents.Extensions.Evals` |
| `QualityFloor` | `Eval__QualityFloor` | `3.0` out of 5 | `Agents.Extensions.Evals` |
| `SafetyEndpoint` | `Eval__SafetyEndpoint` | unset (safety tier skipped) | `Agents.Extensions.Evals` |
| `StorageRoot` | `Eval__StorageRoot` | `eval-store/` beside the test binary | `Agents.Extensions.Evals` |
| `ExecutionName` | `Eval__ExecutionName` | `local-<timestamp>` | `Agents.Extensions.Evals` |
| `CacheTimeToLive` | `Eval__CacheTimeToLive` | `14.00:00:00` | `Agents.Extensions.Evals` |
| `ReportDirectory` | `Eval__ReportDirectory` | `eval-reports/` beside the test binary | `Agents.Api.Evals` |
| `ReportFormat` | `Eval__ReportFormat` | `All` (`GateSummary`, `Json`, `Html`, comma-separated) | `Agents.Api.Evals` |

Every one of them is now defined here — there is no second place to look. The defaults for `Model`
and `BaseUrl` match `Agents.Api`'s own `appsettings.json`, so an unconfigured run measures the
deployment the API actually uses.

The "read by" column records who happens to consume each knob today, not a restriction. A knob only
one suite reads is still defined once and layered the same way, so it cannot come to mean two
things the way `BaseUrl` did.

### Why the options object rather than reads at the point of use

Types and defaults live on `EvalOptions`, so `SampleSize` is an `int` everywhere and
`ReportFormat` is the flags enum — parsing, defaulting and range checking happen once, at the edge,
instead of at each call site. The whole object is validated on load with DataAnnotations
(`[Required]`, `[Range]`, `[Url]`), and a bad value throws an `OptionsValidationException` naming
the variable to set. A suite that runs with a nonsensical floor still reports a verdict, and the
verdict is meaningless — so a nonsensical floor has to fail before the run, not during it.

> **Renamed.** These knobs were previously read as flat `EVAL_*` variables (`EVAL_SAMPLE_SIZE`,
> `EVAL_API_KEY`, …). Those names are no longer read — the section spellings above replace them.
> Two values also changed shape: `Eval__LiveModelEnabled` takes `true`, not `1`, and
> `Eval__ReportFormat` takes the enum member names (`GateSummary`) rather than `gate-summary`.

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
