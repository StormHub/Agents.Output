# Agents.Api.Evals

Evaluation suite for the weather agent, built on the evaluation API that ships inside
`Microsoft.Agents.AI` (no extra package — `Microsoft.Extensions.AI.Evaluation` arrives
transitively).

## Why these checks

The agent is a tool router, not a prose generator, so the signal is in whether it called the
right tool with sane arguments — not in how the answer reads. Every check is local: it reads the
conversation `EvaluateAsync` captured and needs no judge model, so the suite is free and fast.

| Check | Catches |
|---|---|
| `called_weather_tool` | Answering a weather question without looking anything up |
| `grounded_on_calendar` | Resolving "tomorrow" from the model's stale idea of today instead of `GetToday` |
| `answered` | Empty or stub turns |
| `no_ungrounded_weather_claim` | Stating a temperature with no tool call behind it |
| `plausible_coordinates` | Transposed or invented latitude/longitude |
| `answer_names_location` | Looking up one city and answering about another |

## Three tiers

**Scripted** (`ScriptedAgentEvalTests`) — runs offline and deterministically against a scripted
`IChatClient` and canned tools. Nothing here is stochastic: the model's turns are fixed, so
all-must-pass is the correct assertion. It locks the contract the evaluation depends on (tool
names, argument names, how a run becomes an `EvalItem`) and proves the checks actually fire,
including a negative control where a fabricated answer must fail. A green scripted suite means a
red live suite is the model's fault, not the harness's.

**Gate arithmetic** (`Probabilistic/EvalGateTests`) — deterministic tests of the rate and
confidence-bound maths below. If this arithmetic is wrong the live suite gates on nonsense.

**Live** (`LiveModelEvalTests`) — measures the real agent against a real model and the real
Open-Meteo call. Skipped unless enabled:

```bash
EVAL_LIVE_MODEL=1 dotnet test tests/Agents.Api.Evals
```

| Variable | Default |
|---|---|
| `EVAL_LIVE_MODEL` | unset (live measurement skipped) |
| `EVAL_OLLAMA_MODEL` | `qwen3.5` |
| `EVAL_OLLAMA_BASEURL` | `http://localhost:11434` |
| `EVAL_SAMPLE_SIZE` | `30` runs per query |
| `EVAL_REPORT_DIR` | `eval-reports/` beside the test binary |

A full live run makes several hundred model calls. That is the cost of measuring a rate, and it
is why this belongs on a schedule rather than on every pull request.

## How the live tier handles a stochastic agent

An agent that routes correctly 95% of the time is a good agent. A suite that demands 100% would
fail it about a quarter of the time on a 5-run sample, for no reason — repetitions multiply, so
demanding every one of *n* runs pass turns a 0.95 agent into a 0.95ⁿ gate. So the live tier does
not assert that everything passed. It measures.

**Rates, per check.** `AgentEvaluationResults` only exposes `Passed`/`Failed`/`Total`/`AllPassed`,
and an item counts as failed if *any* check failed, so one flaky check hides every other result.
`EvalRates.PerCheck` splits the batch by check so each one gets its own rate.

**Floors, not equality.** Each check declares a floor in `LiveModelEvalTests.Floors`, with the
reasoning attached. Two kinds:

- **Invariant** (floor `1.0`) — a failure is a defect and no sampling argument excuses it.
  `no_ungrounded_weather_claim` and `plausible_coordinates` are invariants: inventing a
  temperature or emitting an out-of-range coordinate is wrong at any rate.
- **Rate** (floor below `1.0`) — the agent is allowed to miss sometimes. `called_weather_tool` at
  0.80 tolerates a model that occasionally skips the tool while still catching a real regression.

Which check belongs in which bucket is the whole judgement call. These are starting points; move
them once there is baseline data for the model you actually run.

**Confidence, not raw counts.** Rate floors are judged against the lower bound of the 95% Wilson
score interval, not the observed rate. Five out of five *looks* like 100% but is consistent with
a true rate near 57%, and gating on the observed figure would read that as proof. The bound
refuses to. Wilson is used rather than the normal approximation because it stays well behaved at
small samples and at rates near 0 or 1 — exactly where agent evaluation lives.

This makes the sample-size requirement explicit: for a flawless sample the bound reduces to
`n / (n + z²)`, so an 80% floor needs at least 16 runs and a 90% floor needs 35.
`EvalGate.MinimumSampleFor(floor)` computes it.

**A report, not just a verdict.** Every live run writes JSON to `EVAL_REPORT_DIR` with per-check
counts, observed rates, lower bounds and floors. A pass/fail answers "should this build go red";
it does not answer "is the agent getting better or worse", which is what evaluation is for. The
report is the deliverable and the gate is a side effect. For a fuller history and HTML reporting,
`Microsoft.Extensions.AI.Evaluation.Reporting` (`ReportingConfiguration`, `ScenarioRun`,
`DiskBasedResultStore`) consumes the same MEAI `EvaluationResult` these results are built from.

**A missing check fails.** A floor naming a check the evaluator never emits — a typo, a renamed
check — is reported as a violation rather than passing silently, so the gate can't quietly become
weaker than it looks.

## Two sharp edges in the framework

**Check names collide.** `LocalEvaluator` keys its metrics by `EvalCheckResult.CheckName`, and
the built-in checks hard-code theirs — every `ToolCalledCheck` variant reports
`tool_called_check`. Two unrenamed checks of the same kind silently overwrite each other, and
only the last survives. `FunctionEvaluator.Create` does not fix this: it only fills in a `null`
name, which the built-ins never return. `WeatherAgentChecks.Named()` rewrites the name via
`with`, and `EveryCheckIsReported` guards it.

**`AssertScoreAtLeast` is a no-op here.** It looks like the right API for threshold gating, but
it walks `DetailedItems`, which only Foundry populates. With `LocalEvaluator` it passes silently
forever. Rates are computed from the metrics instead.

**Do not gate on coordinates.** `ToolCallArgsMatch` compares arguments by exact equality after
unwrapping JSON. Latitude and longitude are model-chosen and vary run to run, so asserting on
them produces a flaky gate. Assert on `location` and let `plausible_coordinates` range-check the
rest.

## Adding a scenario

Add a `WeatherScenario` (query, ordered tool calls, final answer) and pass it to
`ScriptedChatClient`. The client is stateless — it decides what to emit by inspecting the
conversation rather than counting calls — so repeated and concurrent runs cannot interleave. Each
tool may appear at most once per scenario.
