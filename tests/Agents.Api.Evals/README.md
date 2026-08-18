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

## Two tiers

**Scripted** (`ScriptedAgentEvalTests`) — runs offline and deterministically against a scripted
`IChatClient` and canned tools. It does not measure model quality; the model's turns are fixed.
It locks the contract the evaluation depends on (tool names, argument names, how a run becomes an
`EvalItem`) and proves the checks actually fire, including a negative control where a fabricated
answer must fail the gate. A green scripted suite means a red live suite is the model's fault,
not the harness's.

**Live** (`LiveModelEvalTests`) — the same checks against the real agent, a real model and the
real Open-Meteo call. Skipped unless enabled:

```bash
EVAL_LIVE_MODEL=1 dotnet test tests/Agents.Api.Evals
```

| Variable | Default |
|---|---|
| `EVAL_LIVE_MODEL` | unset (live tests skipped) |
| `EVAL_OLLAMA_MODEL` | `qwen3.5` |
| `EVAL_OLLAMA_BASEURL` | `http://localhost:11434` |

`ToolRoutingIsConsistentAcrossRepeatedRuns` uses `numRepetitions: 5`, since a small local model
often routes correctly once and skips the tool on the next attempt.

## Two sharp edges worth knowing

**Check names collide.** `LocalEvaluator` keys its metrics by `EvalCheckResult.CheckName`, and
the built-in checks hard-code theirs — every `ToolCalledCheck` variant reports
`tool_called_check`. Two unrenamed checks of the same kind silently overwrite each other, and
only the last survives. `FunctionEvaluator.Create` does not fix this: it only fills in a `null`
name, which the built-ins never return. `WeatherAgentChecks.Named()` rewrites the name via
`with`, and `EveryCheckIsReported` guards it.

**Do not gate on coordinates.** `ToolCallArgsMatch` compares arguments by exact equality after
unwrapping JSON. Latitude and longitude are model-chosen and vary run to run, so asserting on
them produces a flaky gate. Assert on `location` and let `plausible_coordinates` range-check the
rest.

## Adding a scenario

Add a `WeatherScenario` (query, ordered tool calls, final answer) and pass it to
`ScriptedChatClient`. The client is stateless — it decides what to emit by inspecting the
conversation rather than counting calls — so repeated and concurrent runs cannot interleave. Each
tool may appear at most once per scenario.
