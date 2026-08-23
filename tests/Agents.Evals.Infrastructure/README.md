# Agents.Evals.Infrastructure

What both evaluation suites need, so neither owns a private copy of it.

`Agents.Api.Evals` measures the Agent Framework agent the API hosts. `Agents.Extensions.Evals`
measures the `Microsoft.Extensions.AI` chat pipeline underneath it. They ask different questions,
but they ask them of the same agent, against the same tool contract, over the same cases — and
those are exactly the parts that must not drift apart, because two copies of a scenario silently
become two different tests of two different systems.

| Type | What it is |
|---|---|
| `EvalEnvironment` | every `EVAL_*` knob: store directory, execution name, model, judge model, endpoint, sample size, quality floor, safety endpoint |
| `AgentContract` | the production agent's name and instructions — the only place that reaches into `Agents.Api`'s internals |
| `StubWeatherTools` | canned stand-ins for `GetToday` and `GetWeatherForecast`, mirroring production's names, parameters and return types |
| `WeatherScenario`, `ScriptedToolCall` | one evaluated case: query, scripted tool calls, scripted answer, reference answers |
| `WeatherScenarios` | the three cases both suites run — Tokyo, Paris, and the ungrounded Berlin control |
| `ScriptedChatClient` | an `IChatClient` that replays a scenario instead of calling a model |
| `ToolResults` | pulls tool results out of a completed turn, as text or as numbers |

Deliberately not here: anything either suite gates on. Checks, evaluators, floors, reporting
configuration and store-writing stay with the suite that owns them — sharing a fixture is safe,
sharing a verdict is not.

This project is a plain library. It has no xunit reference, so nothing in it can quietly become a
test, and neither suite inherits a runner from it.
