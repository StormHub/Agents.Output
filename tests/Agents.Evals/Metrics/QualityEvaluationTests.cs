using Agents.Evals.Infrastructure;
using Agents.Evals.Metrics.Evaluators;
using Agents.Evals.Scenarios;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
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
/// Set <c>Eval__LiveModelEnabled=true</c> with an endpoint and key configured to enable. Judge
/// responses are cached, so re-running an unchanged scenario costs nothing.
/// </para>
/// <para>
/// The pipeline and the judge both come from the <see cref="EvalServices"/> class fixture, which
/// builds a container per deployment and disposes them — and every client resolved from them — once
/// the last test in this class has run. Nothing here disposes a client of its own: the two point at
/// the same deployment by default, and a test that tore its client down would take the connection
/// pool the next one reuses with it.
/// </para>
/// </remarks>
public sealed class QualityEvaluationTests(ITestOutputHelper output, EvalServices services)
    : IClassFixture<EvalServices>
{
    private const string SkipReason =
        "Live model evaluation is off. Set Eval__LiveModelEnabled=true, with Eval__BaseUrl and "
        + "Eval__ApiKey pointing at a reachable deployment, to enable it.";

    /// <summary>
    /// Grades each answer on relevance, coherence, fluency, groundedness and tool use, against the
    /// floor in <see cref="EvalOptions.QualityFloor"/>.
    /// </summary>
    [Fact]
    public async Task Answers_ClearTheQualityFloor()
    {
        Assert.SkipUnless(EvalEnvironment.Current.LiveModelEnabled, SkipReason);

        var cancellationToken = TestContext.Current.CancellationToken;

        var judgeClient = WeatherChatPipeline.CreateJudge(services);
        var client = WeatherChatPipeline.CreateLive(services);

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
                $"{EvalEnvironment.Current.Model} answered \"{scenario.Query}\" without calling a tool, so there is nothing to grade.");

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
        Assert.SkipUnless(EvalEnvironment.Current.LiveModelEnabled, SkipReason);

        var cancellationToken = TestContext.Current.CancellationToken;

        var judgeClient = WeatherChatPipeline.CreateJudge(services);
        var client = WeatherChatPipeline.CreateLive(services);

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
}
