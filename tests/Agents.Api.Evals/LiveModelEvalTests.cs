using Agents.Api.Evals.Infrastructure;
using Microsoft.Agents.AI;
using Xunit;

namespace Agents.Api.Evals;

/// <summary>
/// Runs the same checks against the real agent and a live model.
/// </summary>
/// <remarks>
/// Skipped unless <c>EVAL_LIVE_MODEL=1</c>, because these tests need Ollama running and reach
/// Open-Meteo over the network. Override the endpoint with <c>EVAL_OLLAMA_MODEL</c> and
/// <c>EVAL_OLLAMA_BASEURL</c>.
/// </remarks>
public sealed class LiveModelEvalTests
{
    private const string SkipReason =
        "Live model evaluation is off. Set EVAL_LIVE_MODEL=1 with Ollama running to enable it.";

    [Fact]
    public async Task WeatherQueries_PassBaselineChecks()
    {
        Assert.SkipUnless(EvalAgentFactory.LiveModelEnabled, SkipReason);

        var agent = EvalAgentFactory.CreateLive();

        string[] queries =
        [
            "What's the weather in Tokyo?",
            "How warm is it in Reykjavik right now?",
            "Give me the current conditions for Buenos Aires.",
        ];

        var results = await agent.EvaluateAsync(
            queries,
            WeatherAgentChecks.BaselineEvaluator(),
            evalName: "weather-baseline-live");

        results.AssertAllPassed();
    }

    [Fact]
    public async Task DateRelativeQueries_AreGroundedOnTheCalendarTool()
    {
        Assert.SkipUnless(EvalAgentFactory.LiveModelEnabled, SkipReason);

        var agent = EvalAgentFactory.CreateLive();

        string[] queries =
        [
            "Will it rain in Paris tomorrow?",
            "What's the forecast for Sydney this weekend?",
        ];

        var results = await agent.EvaluateAsync(
            queries,
            new LocalEvaluator(WeatherAgentChecks.GroundedOnCalendar()),
            evalName: "date-grounding-live");

        results.AssertAllPassed();
    }

    [Fact]
    public async Task ToolRoutingIsConsistentAcrossRepeatedRuns()
    {
        Assert.SkipUnless(EvalAgentFactory.LiveModelEnabled, SkipReason);

        var agent = EvalAgentFactory.CreateLive();

        // A small local model can route correctly once and skip the tool the next time, so the
        // same query is run several times and every run has to hold up.
        var results = await agent.EvaluateAsync(
            ["What's the weather in Tokyo?"],
            WeatherAgentChecks.BaselineEvaluator(),
            evalName: "tool-routing-consistency",
            numRepetitions: 5);

        Assert.Equal(5, results.Total);
        results.AssertAllPassed();
    }
}
