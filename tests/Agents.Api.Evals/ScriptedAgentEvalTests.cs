using Agents.Api.Evals.Infrastructure;
using Agents.Evals.Infrastructure;
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
    // The scenarios live in Agents.Evals.Infrastructure: the sibling Agents.Extensions.Evals
    // suite scripts the same three cases against the layer below, and a private copy here would
    // let the two drift into measuring different agents.

    [Fact]
    public async Task WeatherQueries_PassEveryBaselineCheck()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(WeatherScenarios.Tokyo, WeatherScenarios.Paris));

        var results = await agent.EvaluateAsync(
            [WeatherScenarios.Tokyo.Query, WeatherScenarios.Paris.Query],
            WeatherAgentChecks.BaselineEvaluator(),
            evalName: "weather-baseline");

        Assert.Equal(2, results.Total);
        results.AssertAllPassed();
    }

    [Fact]
    public async Task DateRelativeQuery_IsGroundedOnTheCalendarTool()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(WeatherScenarios.Paris, WeatherScenarios.Tokyo));

        var grounded = await agent.EvaluateAsync(
            [WeatherScenarios.Paris.Query],
            new LocalEvaluator(WeatherAgentChecks.GroundedOnCalendar()),
            evalName: "date-grounding");

        Assert.True(grounded.AllPassed);

        // The same check must fail for a run that skipped the calendar tool, otherwise it is
        // asserting nothing.
        var ungrounded = await agent.EvaluateAsync(
            [WeatherScenarios.Tokyo.Query],
            new LocalEvaluator(WeatherAgentChecks.GroundedOnCalendar()),
            evalName: "date-grounding-control");

        Assert.False(ungrounded.AllPassed);
    }

    [Fact]
    public async Task ExpectedToolCalls_MatchOnLocationArgument()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(WeatherScenarios.Tokyo));

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
            [WeatherScenarios.Tokyo.Query],
            new LocalEvaluator(EvalChecks.ToolCallArgsMatch()),
            evalName: "tool-arguments",
            expectedToolCalls: expectedToolCalls);

        results.AssertAllPassed();
    }

    [Fact]
    public async Task UngroundedWeatherClaim_FailsTheGate()
    {
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(WeatherScenarios.UngroundedBerlin));

        var results = await agent.EvaluateAsync(
            [WeatherScenarios.UngroundedBerlin.Query],
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
        var agent = EvalAgentFactory.CreateScripted(new ScriptedChatClient(WeatherScenarios.Tokyo));

        var results = await agent.EvaluateAsync(
            [WeatherScenarios.Tokyo.Query],
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
