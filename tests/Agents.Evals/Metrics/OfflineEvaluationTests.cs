using Agents.Evals.Infrastructure;
using Agents.Evals.Metrics.Evaluators;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.NLP;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Xunit;

namespace Agents.Evals.Metrics;

/// <summary>
/// The tier that runs everywhere: no model, no network, no Azure subscription.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of evaluator run here. <c>BLEU</c>, <c>GLEU</c> and <c>F1</c> compare the answer
/// against reference answers by token overlap, and <see cref="WeatherGroundingEvaluator"/> applies
/// this project's own rule. Neither needs a judge, so both are cheap enough to gate every commit.
/// </para>
/// <para>
/// What these tests measure is the harness, not the model — the model's turns are scripted. A green
/// run here means the contexts reach the evaluators, the metrics carry interpretations, and the
/// results land in the store; so when the judged tier goes red, it is the model's fault and not the
/// wiring's.
/// </para>
/// </remarks>
public sealed class OfflineEvaluationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task GroundedAnswers_ScoreAgainstTheirReferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        foreach (var scenario in WeatherScenarios.Grounded)
        {
            var result = await EvaluateAsync(scenario, $"offline.{scenario.Name}", cancellationToken);

            EvaluationReporting.Report(output, scenario.Name, result);
            EvaluationReporting.AssertNoDiagnosticErrors(result);

            // Each scenario lists its scripted answer as the first reference, so a scripted run
            // matches one reference exactly and these scores are 1.0 by construction. The assertion
            // is that the contexts arrived and the scores were computed — the numbers only start
            // carrying information in the live tier, where the answer is not the reference.
            Assert.True(result.Get<NumericMetric>(BLEUEvaluator.BLEUMetricName).Value >= 0.9);
            Assert.True(result.Get<NumericMetric>(GLEUEvaluator.GLEUMetricName).Value >= 0.9);
            Assert.True(result.Get<NumericMetric>(F1Evaluator.F1MetricName).Value >= 0.9);

            Assert.True(
                result.Get<BooleanMetric>(WeatherGroundingEvaluator.GroundedWeatherClaimMetricName).Value);

            EvaluationReporting.AssertNoFailures(result);
        }
    }

    /// <summary>
    /// The control. A grounding check that cannot fail is asserting nothing, so this run has to go
    /// red.
    /// </summary>
    [Fact]
    public async Task InventedReadings_FailTheGroundingMetric()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scenario = WeatherScenarios.UngroundedBerlin;

        var result = await EvaluateAsync(scenario, $"offline.{scenario.Name}", cancellationToken);

        EvaluationReporting.Report(output, scenario.Name, result);

        var grounding = result.Get<BooleanMetric>(WeatherGroundingEvaluator.GroundedWeatherClaimMetricName);
        Assert.False(grounding.Value);
        Assert.True(grounding.Interpretation?.Failed);

        // The overlap scores discriminate too: this answer is measured against the answer the tools
        // would have supported, and shares little with it.
        Assert.True(result.Get<NumericMetric>(BLEUEvaluator.BLEUMetricName).Value < 0.9);
    }

    /// <summary>
    /// Proves the half of the library that is easy to assume: disposing the scenario run writes a
    /// record a report can be built from.
    /// </summary>
    [Fact]
    public async Task Results_LandInTheReportingStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string scenarioName = "offline.store-roundtrip";

        var result = await EvaluateAsync(WeatherScenarios.Tokyo, scenarioName, cancellationToken);

        var store = new DiskBasedResultStore(EvalEnvironment.StorageRoot);
        var stored = new List<ScenarioRunResult>();

        await foreach (var record in store.ReadResultsAsync(
                           EvalEnvironment.ExecutionName,
                           scenarioName,
                           cancellationToken: cancellationToken))
        {
            stored.Add(record);
        }

        var persisted = Assert.Single(stored);

        Assert.Equal(
            result.Metrics.Keys.OrderBy(name => name, StringComparer.Ordinal),
            persisted.EvaluationResult.Metrics.Keys.OrderBy(name => name, StringComparer.Ordinal));

        // The conversation is stored with the result, which is what makes a report readable months
        // later: the score and the turn that produced it sit together.
        Assert.NotEmpty(persisted.Messages);
        Assert.Contains(persisted.ModelResponse.Messages, message => message.Contents.Count > 0);

        output.WriteLine(
            $"Read back {persisted.ScenarioName} / {persisted.IterationName} from {EvalEnvironment.StorageRoot}");
    }

    private static async Task<EvaluationResult> EvaluateAsync(
        WeatherScenario scenario,
        string scenarioName,
        CancellationToken cancellationToken)
    {
        var reporting = EvaluationReporting.ForOfflineChecks();
        using var client = WeatherChatPipeline.CreateScripted(scenario);

        // Disposing the scenario run is what persists the result, so it has to outlive the
        // evaluation and nothing else.
        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            scenarioName,
            cancellationToken: cancellationToken);

        var (messages, response) = await WeatherChatPipeline.RunAsync(client, scenario.Query, cancellationToken);

        return await scenarioRun.EvaluateAsync(
            messages,
            response,
            additionalContext:
            [
                // BLEU scores against the best-matching reference, so the whole list helps it:
                // more acceptable phrasings can only raise the score. GLEU pools every reference
                // into one n-gram bag instead, so a second reference *lowers* an otherwise exact
                // match (measured: 1.0 with one reference, 0.71 with two). It therefore gets the
                // primary reference only, the same one F1 takes.
                new BLEUEvaluatorContext(scenario.References),
                new GLEUEvaluatorContext([scenario.References[0]]),
                new F1EvaluatorContext(scenario.References[0]),
            ],
            cancellationToken: cancellationToken);
    }
}
