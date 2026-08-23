# Agents.Extensions.Evals

Evaluates a **Microsoft.Extensions.AI** chat pipeline with the
**Microsoft.Extensions.AI.Evaluation** family.

This is a different subject from `tests/Agents.Api.Evals`, which measures the Agent Framework
agent the API hosts with Agent Framework's own evaluators. Here the system under test is the
layer underneath — an `IChatClient`, the function-invocation middleware, and the tool contract —
because that is the shape the Evaluation libraries grade: they take `ChatMessage`s and a
`ChatResponse` and know nothing about agents. Both suites write to the same result-store format,
so one `dotnet aieval report` covers both.

## What runs

| Tier | Evaluators | Needs | Gate |
|---|---|---|---|
| Offline (`OfflineEvaluationTests`) | `BLEU`, `GLEU`, `F1`, `WeatherGroundingEvaluator` | nothing | always on |
| Quality (`QualityEvaluationTests`) | `Relevance`, `Coherence`, `Groundedness`, `ToolCallAccuracy`, `TaskAdherence`, `Equivalence` | Ollama | `EVAL_LIVE_MODEL=1` |
| Safety (`SafetyEvaluationTests`) | `HateAndUnfairness`, `Violence`, `SelfHarm`, `Sexual`, `ProtectedMaterial` | Azure AI Foundry project | `EVAL_SAFETY_ENDPOINT` |

```bash
dotnet test tests/Agents.Extensions.Evals                     # offline only
EVAL_LIVE_MODEL=1 dotnet test tests/Agents.Extensions.Evals   # also the judged tiers
```

## How the pieces fit

```
IChatClient (Ollama or scripted)
  └── UseFunctionInvocation ── GetToday, GetWeatherForecast
        └── messages + ChatResponse
              └── ScenarioRun.EvaluateAsync(messages, response, additionalContext)
                    ├── evaluators from the ReportingConfiguration
                    └── dispose ──> result store ──> dotnet aieval report
```

`ReportingConfiguration` is the reusable half — evaluators, store, response cache, and the
interpreter that turns a raw score into a pass or a failure. A `ScenarioRun` is the per-case half;
**disposing it is what persists the result**, so it has to outlive the evaluation.

`EvaluationContext` is how ground truth reaches an evaluator. Each one is matched by type, so the
list handed to `EvaluateAsync` is unordered and extra entries are ignored:

| Context | Carries | Used by |
|---|---|---|
| `BLEUEvaluatorContext`, `GLEUEvaluatorContext` | reference answers | BLEU, GLEU |
| `F1EvaluatorContext` | one ground-truth answer | F1 |
| `GroundednessEvaluatorContext` | the text the answer had to stay inside | Groundedness |
| `EquivalenceEvaluatorContext` | the correct answer | Equivalence |
| `ToolCallAccuracyEvaluatorContext`, `TaskAdherenceEvaluatorContext` | the `AITool`s the model was given | tool-use judges |

The tools are canned (`StubWeatherTools`), which is what makes the judged tiers gradeable: because
the readings are fixed and known, there is a real ground truth to hand `Groundedness` and
`Equivalence`. Against live Open-Meteo there would be nothing to compare against that was not
itself fetched from the same source.

## Three things worth knowing

**Scores are interpreted, and the defaults are strict.** Every shipped evaluator sets its own
`EvaluationMetricInterpretation`: Quality fails anything under 4.0 out of 5, NLP under 0.5, safety
above a severity of 2. `EvaluationReporting.Interpret` overrides only the 1-to-5 judged metrics —
against the floor in `EVAL_QUALITY_FLOOR`, default 3.0, because a small local model rarely clears
4.0 and a suite that is always red stops being read. Everything else returns `null`, which leaves
the evaluator's own interpretation alone; overwriting it would grade a 0-to-7 severity on a
1-to-5 rubric.

**The offline scores are 1.0 by construction.** Each scenario lists its scripted answer as its
first reference, so a scripted run matches a reference exactly. That tier asserts the wiring —
contexts reach the evaluators, metrics carry interpretations, records land in the store — not the
prose. `WeatherGroundingEvaluator` is the one offline check that can genuinely fail, and
`InventedReadings_FailTheGroundingMetric` exists to prove it does.

**Judge responses are cached, tool results are not.** `enableResponseCaching: true` puts a cache
in front of the judge, keyed on scenario, iteration, the judge model and the request — so
re-running an unchanged red suite re-reads verdicts instead of paying for them. The pipeline under
test is *not* behind that cache: it runs for real every time. If you want its turns recorded in the
report as well, run it through `scenarioRun.ChatConfiguration.ChatClient`, which is the wrapped
client `CreateScenarioRunAsync` hands back — here that client is configured for the judge, so the
system under test uses its own.

## Environment

| Variable | Default | Meaning |
|---|---|---|
| `EVAL_STORE_DIR` | `bin/.../eval-store` | result store; shared with `Agents.Api.Evals` if you point both at it |
| `EVAL_EXECUTION_NAME` | `local-<timestamp>` | groups one run in the report; set to the CI build number |
| `EVAL_LIVE_MODEL` | unset | `1` enables the judged tiers |
| `EVAL_OLLAMA_MODEL` | `qwen3.5` | model under test |
| `EVAL_JUDGE_MODEL` | same as above | grading model — a judge sharing the system's blind spots will not see them |
| `EVAL_OLLAMA_BASEURL` | `http://localhost:11434` | Ollama endpoint |
| `EVAL_QUALITY_FLOOR` | `3.0` | pass mark for judged metrics, out of 5 |
| `EVAL_SAFETY_ENDPOINT` | unset | Azure AI Foundry project endpoint; enables the safety tier |

## Reading the results

```bash
dotnet tool restore
dotnet aieval report --path <EVAL_STORE_DIR> -o eval-report.html --open
dotnet aieval report --path <EVAL_STORE_DIR> -n 20 -f json     # trend the last 20 executions
dotnet aieval clean-cache --path <EVAL_STORE_DIR>              # drop expired judge responses
```

Every test prints the absolute store path and execution name it used, since the default lands
under `bin/`.
