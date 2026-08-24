# Agents.Extensions.Evals

Evaluation suite for the **chat pipeline** underneath the weather agent, built on the
`Microsoft.Extensions.AI.Evaluation` family: `Quality` for LLM-judged scores, `NLP` for
reference-overlap scores, `Safety` for Azure AI Foundry content checks, and `Reporting` for the
result store, the response cache and the HTML report.

## Why this suite exists next to `Agents.Api.Evals`

Two suites, two layers of the same system.

| | `Agents.Api.Evals` | `Agents.Extensions.Evals` |
|---|---|---|
| Subject | the `ChatClientAgent` the API hosts | the `IChatClient` pipeline it is built on |
| Library | Agent Framework's evaluation API | `Microsoft.Extensions.AI.Evaluation` |
| Unit | `EvalItem`, batch-scored by `LocalEvaluator` | `ChatMessage`s + `ChatResponse`, item-scored by `IEvaluator` |
| Question | did it route to the right tool with sane arguments? | is the answer relevant, coherent, grounded, and close to the right one? |
| Cost | free, local, deterministic | judge calls, except the offline tier |

The split is not arbitrary. The Evaluation libraries know nothing about agents — they take a
conversation and a response — so an agent is not the thing they can grade. The Agent Framework's
own evaluators know about tool trajectories, which is what a router has to get right. Each library
is pointed at the layer it can actually see.

The consequence is that neither suite subsumes the other. A perfect score here says nothing about
whether the hosted agent routes correctly; a green router says nothing about whether the answer
was grounded in what the tool returned.

## What is evaluated

| Metric | Tier | Catches |
|---|---|---|
| `Grounded Weather Claim` | offline | Reporting a temperature that appears in no tool result |
| `BLEU`, `GLEU`, `F1` | offline | An answer drifting away from the reference wording |
| `Relevance` | quality | Answering a question other than the one asked |
| `Coherence` | quality | An answer that does not hold together |
| `Groundedness` | quality | Prose that goes beyond what the tools returned |
| `Tool Call Accuracy` | quality | Calling the wrong tool, or the right one with wrong arguments |
| `Task Adherence` | quality | Wandering off the task the tools were given for |
| `Equivalence` | quality | An answer that reads well and is wrong |
| `Hate And Unfairness`, `Violence`, `Self Harm`, `Sexual`, `Protected Material` | safety | Content harms and protected material in the model's own words |

`Grounded Weather Claim` is the one check written here rather than shipped. It is the failure this
pipeline exists to avoid — a model answering "it's 25°C in Berlin" from its weights — and no
general-purpose evaluator knows that. It is also the only evaluator in the suite that needs
neither a model nor a reference, so it can gate every commit.

## Three tiers

**Offline** (`OfflineEvaluationTests`) — a scripted `IChatClient` and canned tools, so the model's
turns are fixed. Runs everywhere: no model, no network, no Azure subscription.

What it measures is the harness, not the model. Each scenario lists its scripted answer as its
first reference, so a scripted run matches a reference exactly and BLEU, GLEU and F1 come out at
1.0 **by construction** — the assertion is that the contexts reached the evaluators, the metrics
carry interpretations, and the records landed in the store. `Grounded Weather Claim` is the one
offline check that can genuinely fail, and `InventedReadings_FailTheGroundingMetric` exists to
prove it does. A green offline tier means a red judged tier is the model's fault, not the wiring's.

**Quality** (`QualityEvaluationTests`) — a model grades the pipeline's answers. These are
measurements, not tests: a judge is a model, so its scores move between runs and a single red
metric is a prompt to look, not proof of a regression. What makes them worth running is that the
questions they answer cannot be asserted.

**Safety** (`SafetyEvaluationTests`) — content harm and protected material, scored by the Azure AI
Foundry evaluation service rather than locally. A weather assistant is not where content harms are
likely, which is the point: this is the tier you keep green so a change in instructions, a new
tool or a swapped model has something to be measured against.

```bash
dotnet test tests/Agents.Extensions.Evals                     # offline only
EVAL_LIVE_MODEL=1 dotnet test tests/Agents.Extensions.Evals   # also the judged tiers
```

| Variable | Default |
|---|---|
| `EVAL_LIVE_MODEL` | unset (judged tiers skipped) |
| `EVAL_OLLAMA_MODEL` | `qwen3.5` — the model under test |
| `EVAL_JUDGE_MODEL` | the model under test — the model that grades |
| `EVAL_OLLAMA_BASEURL` | `http://localhost:11434` |
| `EVAL_QUALITY_FLOOR` | `3.0` out of 5 |
| `EVAL_SAFETY_ENDPOINT` | unset (safety tier skipped) |
| `EVAL_STORE_DIR` | `eval-store/` beside the test binary |
| `EVAL_EXECUTION_NAME` | `local-<timestamp>` |

Read through `EvalEnvironment` in
[`Agents.Evals.Infrastructure`](../Agents.Evals.Infrastructure/README.md), so both suites see the
same values. Judging with the model under test is the cheapest setup and the weakest one — a judge
that shares the system's blind spots will not see them. Set `EVAL_JUDGE_MODEL` to something
stronger when the scores start mattering.

## How a run works

```
IChatClient (Ollama or scripted)
  └── UseFunctionInvocation ── GetToday, GetWeatherForecast
        └── messages + ChatResponse
              └── ScenarioRun.EvaluateAsync(messages, response, additionalContext)
                    ├── evaluators from the ReportingConfiguration
                    └── dispose ──> result store ──> dotnet aieval report
```

`ReportingConfiguration` is the reusable half — which evaluators run, where results are stored,
whether judge responses are cached, and how a raw score becomes a pass or a failure. A
`ScenarioRun` is the per-case half, created from it. **Disposing the scenario run is what persists
the result**, so it has to outlive the evaluation; `EvaluateAsync` may be called only once on it.

This is the idiomatic path, and worth noting because the sibling suite cannot use it:
`ReportingConfiguration.Evaluators` takes MEAI's `IEvaluator`, which the Agent Framework's
`LocalEvaluator` is not, so `Agents.Api.Evals` writes to the store by hand. Same store, same
report, two routes in.

### Ground truth reaches an evaluator as context

`EvaluationContext` entries are matched by type, so the list handed to `EvaluateAsync` is
unordered and extra entries are ignored. An evaluator that does not find the context it needs
reports an error diagnostic rather than a score.

| Context | Carries | Used by |
|---|---|---|
| `BLEUEvaluatorContext`, `GLEUEvaluatorContext` | reference answers — the best match wins | BLEU, GLEU |
| `F1EvaluatorContext` | one ground-truth answer | F1 |
| `GroundednessEvaluatorContext` | the text the answer had to stay inside | Groundedness |
| `EquivalenceEvaluatorContext` | the correct answer | Equivalence |
| `ToolCallAccuracyEvaluatorContext`, `TaskAdherenceEvaluatorContext` | the `AITool`s the model was given | the tool-use judges |

The tools are canned, which is what makes the judged tiers gradeable: because the readings are
fixed and known, `Groundedness` and `Equivalence` have a real ground truth rather than a plausible
one. Against live Open-Meteo there would be nothing to compare against that was not itself fetched
from the same source.

### Scores are interpreted, and the defaults are strict

Every shipped evaluator sets its own `EvaluationMetricInterpretation` — a rating, and a `Failed`
flag against its own scale:

| Family | Scale | Ships as failing |
|---|---|---|
| Quality | 1–5, higher better | below 4.0 |
| NLP | 0–1, higher better | below 0.5 |
| Safety | 0–7 severity, **lower** better | above 2.0 |

`ReportingConfiguration` takes an interpreter that can override those, and this suite uses it for
exactly one thing: applying `EVAL_QUALITY_FLOOR` (default 3.0) to the 1-to-5 judged metrics,
because a small local model rarely clears 4.0 and a suite that is always red stops being read.

Returning `null` for everything else matters just as much — that leaves the evaluator's own
interpretation alone. Overwriting it would grade a 0-to-7 severity or a 0-to-1 overlap score on a
1-to-5 rubric, which is how a suite ends up reporting a clean safety result as a failure.

## Reporting

All three tiers write to one store under one execution name, so a single report shows the offline
checks, the judged scores and the safety severities of the same run side by side. Scenario names
here are prefixed by tier (`offline.`, `quality.`, `equivalence.`, `safety.`) while the sibling
suite names its scenarios `{evalName}.q00`, so the two cannot collide — point `EVAL_STORE_DIR` at
the same directory for both and one report covers the whole system.

```bash
dotnet tool restore
dotnet aieval report --path <EVAL_STORE_DIR> -o eval-report.html --open
dotnet aieval report --path <EVAL_STORE_DIR> -n 20 -f json     # trend the last 20 executions
dotnet aieval clean-cache --path <EVAL_STORE_DIR>              # drop expired judge responses
```

The default store lands beside the test binary (under `bin/`), which is awkward to point a tool
at — so every test prints the absolute path and execution name it used. Set `EVAL_STORE_DIR`
somewhere stable to accumulate history, and `EVAL_EXECUTION_NAME` to the CI build number so
executions line up run to run.

**Judge responses are cached; the pipeline under test is not.** `enableResponseCaching: true` puts
a cache in front of the judge, keyed on scenario, iteration, the request, and the judge model — so
re-running an unchanged red suite re-reads verdicts instead of paying for them again. The judge
model is in the key deliberately: without it, switching judges would silently serve the previous
judge's opinions. The system under test runs for real every time, which is what you want, since
its output is the thing being measured.

## Sharp edges

**`Tool Call Accuracy` is a `BooleanMetric`, not a score.** It sits among five-point judged
metrics and looks like one, but the quality floor cannot apply to it — the interpreter skips it
and its own pass/fail interpretation stands.

**A response with no tool call is an error, not a low score.** `ToolCallAccuracyEvaluator` adds an
error diagnostic and returns no value when the response contains no `FunctionCallContent`. An
error diagnostic is not a failing metric, so it would otherwise pass silently;
`AssertNoDiagnosticErrors` catches it, and the quality tier asserts on the tool call first so the
test says plainly that the model skipped the tool.

**Only text is graded.** `ChatResponse.Text` concatenates the `TextContent` of every message in
the turn, and tool calls and tool results are not `TextContent` — so the serialized tool payload
never leaks into what BLEU or a judge sees. That is why a scripted answer scores 1.0 against
itself even though the response also carries two tool messages.

**`UseFunctionInvocation` puts the trajectory in the response, not the request.** The tool loop
runs inside `GetResponseAsync`, and the intermediate messages are appended to
`ChatResponse.Messages`. Anything reading tool results — `ToolResults`, the grounding evaluator,
`ToolCallAccuracyEvaluator` — has to look there, not in the messages it sent.

## Adding a scenario

Scenarios, the scripted client, the canned tools and the `EVAL_*` knobs live in
[`Agents.Evals.Infrastructure`](../Agents.Evals.Infrastructure/README.md), shared with
`Agents.Api.Evals`.

Add a `WeatherScenario` to `WeatherScenarios` — name, query, ordered tool calls, scripted answer,
and reference answers. Keep the scripted answer as the first reference unless you mean to measure
the wording itself, and make the numbers in it numbers the canned tools actually return, or the
grounding check will fail the scenario correctly and confusingly.

A scenario added there shows up in the sibling suite too, which is the point: the same case is
then measured at both layers.

## What stays here

Evaluators, the reporting configuration and the metric interpreter — everything this suite gates
on. Sharing a fixture with the sibling suite is safe; sharing a verdict is not.
