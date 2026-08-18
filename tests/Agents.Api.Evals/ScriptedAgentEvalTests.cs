using Agents.Api.Evals.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace Agents.Api.Evals;

/// <summary>
/// Evaluates the agent over a scripted chat client, so the suite is deterministic and runs
/// offline in CI.
/// </summary>
/// <remarks>
/// These tests do not measure how good a model is — the model's turns are fixed. They lock the
/// contract the evaluation depends on (tool names, argument names, how a run is turned into an
/// <see cref="EvalItem"/>) and prove the checks in <see cref="WeatherAgentChecks"/> actually fire,
/// so a green scripted suite means a red live suite is the model's fault and not the harness's.
/// </remarks>
public sealed class ScriptedAgentEvalTests
{
    private const string TokyoQuery = "What's the weather in Tokyo?";
    private const string ParisQuery = "Will it rain in Paris tomorrow?";
    private const string BerlinQuery = "What's the weather in Berlin?";

    private static readonly WeatherScenario TokyoScenario = new(
        TokyoQuery,
        [
            new ScriptedToolCall(
                WeatherAgentChecks.WeatherToolName,
                new Dictionary<string, object?>
                {
                    ["latitude"] = 35.6762,
                    ["longitude"] = 139.6503,
                    ["location"] = "Tokyo",
                }),
        ],
        "It is currently 22.4°C and cloudy in Tokyo, with 63% humidity and a north-easterly breeze.");

    private static readonly WeatherScenario ParisScenario = new(
        ParisQuery,
        [
            new ScriptedToolCall(WeatherAgentChecks.CalendarToolName),
            new ScriptedToolCall(
                WeatherAgentChecks.WeatherToolName,
                new Dictionary<string, object?>
                {
                    ["latitude"] = 48.8566,
                    ["longitude"] = 2.3522,
                    ["location"] = "Paris",
                }),
        ],
        "Tomorrow in Paris looks cloudy with a high of 21°C and no rain expected.");

    /// <summary>A model answering from its weights: confident numbers, no tool call.</summary>
    private static readonly WeatherScenario UngroundedBerlinScenario = new(
        BerlinQuery,
        [],
        "It's 25°C and sunny in Berlin right now, a lovely day to be outside.");

    [Fact]
    public async Task WeatherQueries_PassEveryBaselineCheck()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(TokyoScenario, ParisScenario));

        var results = await agent.EvaluateAsync(
            [TokyoQuery, ParisQuery],
            WeatherAgentChecks.BaselineEvaluator(),
            evalName: "weather-baseline");

        Assert.Equal(2, results.Total);
        results.AssertAllPassed();
    }

    [Fact]
    public async Task DateRelativeQuery_IsGroundedOnTheCalendarTool()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(ParisScenario, TokyoScenario));

        var grounded = await agent.EvaluateAsync(
            [ParisQuery],
            new LocalEvaluator(WeatherAgentChecks.GroundedOnCalendar()),
            evalName: "date-grounding");

        Assert.True(grounded.AllPassed);

        // The same check must fail for a run that skipped the calendar tool, otherwise it is
        // asserting nothing.
        var ungrounded = await agent.EvaluateAsync(
            [TokyoQuery],
            new LocalEvaluator(WeatherAgentChecks.GroundedOnCalendar()),
            evalName: "date-grounding-control");

        Assert.False(ungrounded.AllPassed);
    }

    [Fact]
    public async Task ExpectedToolCalls_MatchOnLocationArgument()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(TokyoScenario));

        // Only the location is asserted. Latitude and longitude are model-chosen and compared by
        // exact equality after a JSON round-trip, which makes them unusable as a gate.
        List<IEnumerable<ExpectedToolCall>> expectedToolCalls =
        [
            [
                new ExpectedToolCall(
                    WeatherAgentChecks.WeatherToolName,
                    new Dictionary<string, object> { ["location"] = "Tokyo" }),
            ],
        ];

        var results = await agent.EvaluateAsync(
            [TokyoQuery],
            new LocalEvaluator(EvalChecks.ToolCallArgsMatch()),
            evalName: "tool-arguments",
            expectedToolCalls: expectedToolCalls);

        results.AssertAllPassed();
    }

    [Fact]
    public async Task UngroundedWeatherClaim_FailsTheGate()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(UngroundedBerlinScenario));

        var results = await agent.EvaluateAsync(
            [BerlinQuery],
            WeatherAgentChecks.BaselineEvaluator(),
            evalName: "ungrounded-control");

        Assert.False(results.AllPassed);
        Assert.Throws<InvalidOperationException>(() => results.AssertAllPassed());

        var item = Assert.Single(results.Items);
        Assert.True(Failed(item, "no_ungrounded_weather_claim"));
        Assert.True(Failed(item, "called_weather_tool"));

        // The answer is fluent and names no location it failed to look up, so the shallow checks
        // still pass — which is exactly why the grounding check has to exist.
        Assert.False(Failed(item, "answered"));
        Assert.False(Failed(item, "answer_names_location"));
    }

    [Fact]
    public async Task EveryCheckIsReported()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(TokyoScenario));

        var results = await agent.EvaluateAsync(
            [TokyoQuery],
            WeatherAgentChecks.BaselineEvaluator(),
            evalName: "check-names");

        // LocalEvaluator keys metrics by check name, so two checks sharing a name collapse into
        // one silently. Guards WeatherAgentChecks.Named().
        var item = Assert.Single(results.Items);
        Assert.Equal(5, item.Metrics.Count);
    }

    private static bool Failed(EvaluationResult result, string checkName) =>
        result.Metrics.TryGetValue(checkName, out var metric) && metric.Interpretation?.Failed == true;
}
