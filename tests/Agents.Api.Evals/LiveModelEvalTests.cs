using Agents.Api.Evals.Infrastructure;
using Agents.Api.Evals.Probabilistic;
using Microsoft.Agents.AI;
using Xunit;

namespace Agents.Api.Evals;

/// <summary>
/// Measures the real agent against a live model.
/// </summary>
/// <remarks>
/// <para>
/// The agent is stochastic, so these are measurements, not tests. Each check is sampled over
/// many runs and judged against a floor rather than required to pass every time: an agent that
/// routes correctly 95% of the time is a good agent, and a suite that demands 100% would fail it
/// roughly a quarter of the time for no reason.
/// </para>
/// <para>
/// Every run writes a report, and that report — not the red/green — is the point. Set
/// <c>EVAL_LIVE_MODEL=1</c> with Ollama running to enable, <c>EVAL_SAMPLE_SIZE</c> to change the
/// sample. A full run makes several hundred model calls, so this belongs on a schedule rather
/// than on every pull request.
/// </para>
/// </remarks>
public sealed class LiveModelEvalTests(ITestOutputHelper output)
{
    private const string SkipReason =
        "Live model evaluation is off. Set EVAL_LIVE_MODEL=1 with Ollama running to enable it.";

    /// <summary>
    /// What each check has to clear.
    /// </summary>
    /// <remarks>
    /// A floor of 1.0 declares an invariant — a failure is a defect, not noise, and no sampling
    /// argument excuses it. Everything else is a rate the agent is allowed to miss sometimes.
    /// Deciding which is which is the whole judgement call; these are starting points to be moved
    /// once there is baseline data for the model in use.
    /// </remarks>
    private static readonly Dictionary<string, CheckFloor> Floors = new(StringComparer.Ordinal)
    {
        ["no_ungrounded_weather_claim"] = new(
            1.0,
            "Inventing a temperature is a defect at any rate — it is the failure this agent exists to avoid."),
        ["plausible_coordinates"] = new(
            1.0,
            "An out-of-range coordinate is a malformed call, not an unlucky sample."),
        ["called_weather_tool"] = new(
            0.80,
            "A small local model skips the tool occasionally; a sustained drop means routing regressed."),
        ["answer_names_location"] = new(
            0.80,
            "Occasional paraphrasing is tolerable; consistently answering about the wrong place is not."),
        ["answered"] = new(
            0.90,
            "Empty turns should be rare."),
    };

    [Fact]
    public async Task WeatherQueries_ClearTheirFloors()
    {
        Assert.SkipUnless(EvalAgentFactory.LiveModelEnabled, SkipReason);

        string[] queries =
        [
            "What's the weather in Tokyo?",
            "How warm is it in Reykjavik right now?",
            "Give me the current conditions for Buenos Aires.",
        ];

        await this.MeasureAsync("weather-baseline", queries, WeatherAgentChecks.BaselineEvaluator());
    }

    [Fact]
    public async Task DateRelativeQueries_AreGroundedOnTheCalendarTool()
    {
        Assert.SkipUnless(EvalAgentFactory.LiveModelEnabled, SkipReason);

        string[] queries =
        [
            "Will it rain in Paris tomorrow?",
            "What's the forecast for Sydney this weekend?",
        ];

        // Grounding is graded as a rate, not an invariant: a model that resolves "tomorrow"
        // itself is wrong but not malformed, and the interesting signal is how often it happens.
        var floors = new Dictionary<string, CheckFloor>(StringComparer.Ordinal)
        {
            ["grounded_on_calendar"] = new(
                0.80,
                "Date-relative queries should reach GetToday; a sustained drop means the model stopped grounding."),
        };

        await this.MeasureAsync(
            "date-grounding",
            queries,
            new LocalEvaluator(WeatherAgentChecks.GroundedOnCalendar()),
            floors);
    }

    /// <summary>
    /// Reports how often the agent passes every check at once on a single query.
    /// </summary>
    /// <remarks>
    /// The joint figure is recorded but never gated — no floor names <c>all_checks</c>. Independent
    /// checks multiply, so it sits below every individual rate and would make a punishing gate.
    /// It is still the honest headline number, and watching it move across runs is how you notice
    /// a model that got worse in a way no single check caught. The per-check floors still apply.
    /// </remarks>
    [Fact]
    public async Task OverallConsistency_IsRecorded()
    {
        Assert.SkipUnless(EvalAgentFactory.LiveModelEnabled, SkipReason);

        var outcome = await this.MeasureAsync(
            "overall-consistency",
            ["What's the weather in Tokyo?"],
            WeatherAgentChecks.BaselineEvaluator());

        output.WriteLine(outcome.Overall.Describe());
    }

    private async Task<MeasurementOutcome> MeasureAsync(
        string scenario,
        IEnumerable<string> queries,
        LocalEvaluator evaluator,
        IReadOnlyDictionary<string, CheckFloor>? floors = null)
    {
        var effectiveFloors = floors ?? Floors;
        var agent = EvalAgentFactory.CreateLive();

        var results = await agent.EvaluateAsync(
            queries,
            evaluator,
            evalName: scenario,
            numRepetitions: EvalAgentFactory.SampleSize);

        var rates = EvalRates.PerCheck(results);
        var outcome = EvalGate.Evaluate(rates, effectiveFloors);

        // Two records, answering different questions. The store keeps every item so the report
        // tool can show the trajectory and compare executions; the summary keeps the derived
        // rates and bounds, which ScenarioRunResult has no place for.
        var storePath = await EvalResultStore.WriteAsync(scenario, results, EvalAgentFactory.LiveModel);
        var summaryPath = EvalReport.Write(scenario, outcome, results, effectiveFloors, EvalAgentFactory.LiveModel);

        output.WriteLine($"{scenario} — {results.Total} runs of {EvalAgentFactory.LiveModel}");
        output.WriteLine(outcome.Report());
        output.WriteLine($"Store:   {storePath} (execution {EvalResultStore.ExecutionName})");
        output.WriteLine($"Summary: {summaryPath}");

        Assert.True(outcome.Passed, outcome.Report());

        return new MeasurementOutcome(outcome, EvalRates.Overall(results));
    }

    private sealed record MeasurementOutcome(GateOutcome Gate, CheckRate Overall);
}
