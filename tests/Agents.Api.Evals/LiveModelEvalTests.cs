using Agents.Api.Evals.Infrastructure;
using Agents.Evals.Infrastructure;
using Agents.Evals.Infrastructure.Probabilistic;
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
        "Live model evaluation is off. Set EVAL_LIVE_MODEL=1 with api configurations to enable it.";

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
        Assert.SkipUnless(EvalEnvironment.LiveModelEnabled, SkipReason);

        string[] queries =
        [
            "What's the weather in Tokyo?",
            "How warm is it in Reykjavik right now?",
            "Give me the current conditions for Buenos Aires.",
        ];

        await MeasureAsync(
            "weather-baseline",
            "Baseline weather queries for three cities, sampled many times. Checks that the " +
            "agent calls the real weather tool, names the right location, never invents a " +
            "temperature, and gives a plausible coordinate — each judged as a rate, not a " +
            "one-shot pass/fail, because the agent is stochastic.",
            queries,
            WeatherAgentChecks.BaselineEvaluator());
    }

    [Fact]
    public async Task DateRelativeQueries_AreGroundedOnTheCalendarTool()
    {
        Assert.SkipUnless(EvalEnvironment.LiveModelEnabled, SkipReason);

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

        await MeasureAsync(
            "date-grounding",
            "Queries that reference a relative date ('tomorrow', 'this weekend'). Checks that " +
            "the agent resolves the date via the calendar tool rather than guessing it itself — " +
            "a model that infers 'tomorrow' on its own can still answer plausibly but is no " +
            "longer grounded in the actual current date.",
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
        Assert.SkipUnless(EvalEnvironment.LiveModelEnabled, SkipReason);

        var outcome = await MeasureAsync(
            "overall-consistency",
            "A single weather query, sampled many times, reporting the joint rate at which the " +
            "agent passes every check at once. This figure is recorded but never gated on its " +
            "own — independent checks multiply, so it sits below every individual rate by " +
            "construction — but it's the honest headline number for watching whether the agent " +
            "is getting better or worse in a way no single check would catch. The per-check " +
            "floors above still apply and can still fail this run.",
            ["What's the weather in Tokyo?"],
            WeatherAgentChecks.BaselineEvaluator());

        output.WriteLine(outcome.Overall.Describe());
    }

    private async Task<MeasurementOutcome> MeasureAsync(
        string scenario,
        string description,
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
            numRepetitions: EvalEnvironment.SampleSize);

        var rates = EvalRates.PerCheck(results);
        var outcome = EvalGate.Evaluate(rates, effectiveFloors);

        var paths = await EvalReport.WriteAsync(
            nameof(LiveModelEvalTests),
            scenario,
            description,
            outcome,
            results,
            effectiveFloors,
            EvalEnvironment.Model,
            EvalEnvironment.ReportFormat);

        output.WriteLine($"{scenario} — {results.Total} runs of {EvalEnvironment.Model}");
        output.WriteLine(outcome.Report());
        output.WriteLine($"Reports: {string.Join(", ", paths)}");

        Assert.True(outcome.Passed, outcome.Report());

        return new MeasurementOutcome(outcome, EvalRates.Overall(results));
    }

    private sealed record MeasurementOutcome(GateOutcome Gate, CheckRate Overall);
}
