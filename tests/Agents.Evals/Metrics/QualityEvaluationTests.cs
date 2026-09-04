using Agents.Evals.Infrastructure;
using Agents.Evals.Metrics.Evaluators;
using Agents.Evals.Scenarios;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agents.Evals.Metrics;

/// <summary>
/// The judged tier: a model grades the pipeline's answers.
/// </summary>
/// <remarks>
/// <para>
/// These are measurements, not tests. A judge is a model, so its scores move between runs; a single
/// red metric here is a prompt to look, not proof of a regression. What makes them worth running is
/// that the questions they answer cannot be asserted — whether an answer is coherent, whether it
/// stayed inside what the tools returned, whether the tool call actually served the request.
/// </para>
/// <para>
/// Set <c>EvaluationOptions__LiveModelEnabled=true</c> with an endpoint and key configured to
/// enable. Judge responses are cached, so re-running an unchanged scenario costs nothing.
/// </para>
/// <para>
/// The pipeline and the judge are both resolved from this class's own container, built in the
/// constructor out of the <see cref="EvaluationSetup"/> fixture's registrations and disposed with
/// the test. Nothing here disposes a client of its own: the container owns every one it hands out,
/// and the two point at the same deployment by default, so a test that tore its client down would
/// take the connection pool the other one reuses with it.
/// </para>
/// </remarks>
public sealed class QualityEvaluationTests(ITestOutputHelper output, EvaluationSetup setup)
    : IClassFixture<EvaluationSetup>, IAsyncDisposable
{
    private const string SkipReason =
        "Live model evaluation is off. Set EvaluationOptions__LiveModelEnabled=true, with "
        + "EvaluationOptions__BaseUrl and EvaluationOptions__ApiKey pointing at a reachable "
        + "deployment, to enable it.";

    private readonly ServiceProvider _provider = setup.Build();

    /// <summary>
    /// Grades each answer on relevance, coherence, fluency, groundedness and tool use, against the
    /// floor in <see cref="EvaluationOptions.QualityFloor"/>.
    /// </summary>
    [Fact]
    public async Task Answers_ClearTheQualityFloor()
    {
        Assert.SkipUnless(EvaluationEnvironment.Current.LiveModelEnabled, SkipReason);

        var cancellationToken = TestContext.Current.CancellationToken;

        var judgeClient = WeatherChatPipeline.CreateJudge(_provider);
        var client = WeatherChatPipeline.CreateLive(_provider);

        var reporting = EvaluationReporting.ForQualityChecks(new ChatConfiguration(judgeClient));

        foreach (var scenario in WeatherScenarios.Grounded)
        {
            await using var scenarioRun = await reporting.CreateScenarioRunAsync(
                $"quality.{scenario.Name}",
                cancellationToken: cancellationToken);

            var (messages, response) = await WeatherChatPipeline.RunAsync(
                client,
                scenario.Query,
                cancellationToken);

            // Tool Call Accuracy reports an error rather than a score when the response contains no
            // tool call, so say plainly what happened before the evaluator's diagnostic does.
            Assert.True(
                response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Any(),
                $"{EvaluationEnvironment.Current.Model} answered \"{scenario.Query}\" without calling a tool, so there is nothing to grade.");

            var result = await scenarioRun.EvaluateAsync(
                messages,
                response,
                additionalContext:
                [
                    // Groundedness is graded against what the tools actually returned, which is
                    // knowable here only because the tools are canned.
                    new GroundednessEvaluatorContext(ToolResults.Render(messages, response)),
                    new ToolCallAccuracyEvaluatorContext(WeatherChatPipeline.Tools),
                    new TaskAdherenceEvaluatorContext(WeatherChatPipeline.Tools),
                ],
                cancellationToken: cancellationToken);

            output.WriteLine($"{scenario.Query} -> {response.Text}");
            EvaluationReporting.Report(output, scenario.Name, result);

            EvaluationReporting.AssertNoDiagnosticErrors(result);
            EvaluationReporting.AssertNoFailures(result);
        }
    }

    /// <summary>
    /// Grades each answer against the answer the canned tool data supports.
    /// </summary>
    /// <remarks>
    /// Relevance and coherence ask whether an answer is good; equivalence and completeness ask
    /// whether it is the right one, and whether it left anything out. Both need a ground truth,
    /// which most evaluation suites do not have — this one does, because the tools return fixed
    /// readings, so the reference answer is correct rather than merely plausible.
    /// </remarks>
    [Fact]
    public async Task Answers_MatchTheReferenceAnswer()
    {
        Assert.SkipUnless(EvaluationEnvironment.Current.LiveModelEnabled, SkipReason);

        var cancellationToken = TestContext.Current.CancellationToken;

        var judgeClient = WeatherChatPipeline.CreateJudge(_provider);
        var client = WeatherChatPipeline.CreateLive(_provider);

        var reporting = EvaluationReporting.ForEquivalenceChecks(new ChatConfiguration(judgeClient));

        foreach (var scenario in WeatherScenarios.Grounded)
        {
            await using var scenarioRun = await reporting.CreateScenarioRunAsync(
                $"equivalence.{scenario.Name}",
                cancellationToken: cancellationToken);

            var (messages, response) = await WeatherChatPipeline.RunAsync(
                client,
                scenario.Query,
                cancellationToken);

            var result = await scenarioRun.EvaluateAsync(
                messages,
                response,
                additionalContext:
                [
                    new EquivalenceEvaluatorContext(scenario.References[0]),
                    new CompletenessEvaluatorContext(scenario.References[0]),
                ],
                cancellationToken: cancellationToken);

            output.WriteLine($"{scenario.Query} -> {response.Text}");
            EvaluationReporting.Report(output, scenario.Name, result);

            EvaluationReporting.AssertNoDiagnosticErrors(result);
            EvaluationReporting.AssertNoFailures(result);
        }
    }

    /// <summary>
    /// Disposes this test's container, and with it every client resolved from it.
    /// </summary>
    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}
