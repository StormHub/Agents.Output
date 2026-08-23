using System.Globalization;
using Agents.Evals.Infrastructure;
using Agents.Extensions.Evals.Evaluators;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.NLP;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.AI.Evaluation.Safety;
using Xunit;

namespace Agents.Extensions.Evals.Infrastructure;

/// <summary>
/// Builds the <see cref="ReportingConfiguration"/> for each tier, and the small helpers the tests
/// use to read a result.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ReportingConfiguration"/> is the reusable half of the suite: which evaluators
/// run, where results are stored, whether judge responses are cached, and how a raw score becomes
/// a pass or a failure. A <c>ScenarioRun</c> — the per-case half — is created from it, and
/// persists the result when it is disposed.
/// </para>
/// <para>
/// All three tiers share one store and one execution name, so a single
/// <c>dotnet aieval report</c> shows the offline checks, the judged scores and the safety
/// severities of the same run side by side.
/// </para>
/// </remarks>
internal static class EvaluationReporting
{
    /// <summary>
    /// The deterministic tier: reference-overlap scores and the domain grounding rule. No model
    /// is involved, so there is no <c>ChatConfiguration</c> and nothing to cache.
    /// </summary>
    public static ReportingConfiguration ForOfflineChecks() =>
        DiskBasedReportingConfiguration.Create(
            storageRootPath: EvalEnvironment.StorageRoot,
            evaluators:
            [
                new BLEUEvaluator(),
                new GLEUEvaluator(),
                new F1Evaluator(),
                new WeatherGroundingEvaluator(),
            ],
            chatConfiguration: null,
            enableResponseCaching: false,
            executionName: EvalEnvironment.ExecutionName,
            evaluationMetricInterpreter: Interpret,
            tags: ["suite:extensions-evals", "tier:offline"]);

    /// <summary>
    /// The judged tier: an LLM grades the answer. Response caching is on, so re-running a red
    /// suite re-reads the judge's verdicts instead of paying for them again.
    /// </summary>
    /// <remarks>
    /// The judge model is a caching key. Without it, switching judges would silently serve the
    /// previous judge's opinions.
    /// </remarks>
    public static ReportingConfiguration ForQualityChecks(ChatConfiguration judge) =>
        DiskBasedReportingConfiguration.Create(
            storageRootPath: EvalEnvironment.StorageRoot,
            evaluators:
            [
                new RelevanceEvaluator(),
                new CoherenceEvaluator(),
                new GroundednessEvaluator(),
                new ToolCallAccuracyEvaluator(),
                new TaskAdherenceEvaluator(),
            ],
            chatConfiguration: judge,
            enableResponseCaching: true,
            timeToLiveForCacheEntries: EvalEnvironment.CacheTimeToLive,
            cachingKeys: [EvalEnvironment.JudgeModel],
            executionName: EvalEnvironment.ExecutionName,
            evaluationMetricInterpreter: Interpret,
            tags:
            [
                "suite:extensions-evals",
                "tier:quality",
                $"model:{EvalEnvironment.Model}",
                $"judge:{EvalEnvironment.JudgeModel}",
            ]);

    /// <summary>
    /// The judged tier again, but graded against a known-correct answer rather than against the
    /// question. Kept separate because it is the one tier that needs ground truth.
    /// </summary>
    public static ReportingConfiguration ForEquivalenceChecks(ChatConfiguration judge) =>
        DiskBasedReportingConfiguration.Create(
            storageRootPath: EvalEnvironment.StorageRoot,
            evaluators: [new EquivalenceEvaluator()],
            chatConfiguration: judge,
            enableResponseCaching: true,
            timeToLiveForCacheEntries: EvalEnvironment.CacheTimeToLive,
            cachingKeys: [EvalEnvironment.JudgeModel],
            executionName: EvalEnvironment.ExecutionName,
            evaluationMetricInterpreter: Interpret,
            tags:
            [
                "suite:extensions-evals",
                "tier:equivalence",
                $"model:{EvalEnvironment.Model}",
                $"judge:{EvalEnvironment.JudgeModel}",
            ]);

    /// <summary>
    /// The safety tier. The "chat configuration" here is not a model at all — it is the Azure AI
    /// Foundry evaluation service wearing an <c>IChatClient</c>, which is how the same
    /// <c>ScenarioRun</c> plumbing carries both.
    /// </summary>
    public static ReportingConfiguration ForSafetyChecks(ChatConfiguration contentSafety) =>
        DiskBasedReportingConfiguration.Create(
            storageRootPath: EvalEnvironment.StorageRoot,
            evaluators:
            [
                new HateAndUnfairnessEvaluator(),
                new ViolenceEvaluator(),
                new SelfHarmEvaluator(),
                new SexualEvaluator(),
                new ProtectedMaterialEvaluator(),
            ],
            chatConfiguration: contentSafety,
            enableResponseCaching: true,
            timeToLiveForCacheEntries: EvalEnvironment.CacheTimeToLive,
            executionName: EvalEnvironment.ExecutionName,
            evaluationMetricInterpreter: Interpret,
            tags: ["suite:extensions-evals", "tier:safety", $"model:{EvalEnvironment.Model}"]);

    /// <summary>Prints every metric in a result, so a red run is readable from the test log.</summary>
    public static void Report(ITestOutputHelper output, string scenarioName, EvaluationResult result)
    {
        output.WriteLine($"--- {scenarioName} ---");

        foreach (var metric in result.Metrics.Values)
        {
            var value = (metric switch
            {
                NumericMetric numeric => numeric.Value?.ToString("0.###", CultureInfo.InvariantCulture),
                BooleanMetric boolean => boolean.Value?.ToString(),
                StringMetric text => text.Value,
                _ => null,
            }) ?? "(no value)";

            var verdict = metric.Interpretation switch
            {
                { Failed: true } interpretation => $"FAILED ({interpretation.Rating})",
                { } interpretation => interpretation.Rating.ToString(),
                _ => "unrated",
            };

            output.WriteLine($"  {metric.Name,-24} {value,-10} {verdict}");

            if (metric.Reason is { Length: > 0 } reason)
            {
                output.WriteLine($"      {reason}");
            }
        }

        output.WriteLine($"  store: {EvalEnvironment.StorageRoot} (execution {EvalEnvironment.ExecutionName})");
    }

    /// <summary>Fails the test if any metric was interpreted as a failure.</summary>
    public static void AssertNoFailures(EvaluationResult result)
    {
        var failures = result.Metrics.Values
            .Where(metric => metric.Interpretation?.Failed is true)
            .Select(metric => $"{metric.Name}: {metric.Interpretation?.Reason ?? metric.Reason}")
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Fails the test if an evaluator reported an error.
    /// </summary>
    /// <remarks>
    /// A judge that timed out or returned unparseable JSON leaves a diagnostic and a metric with
    /// no value — which is not a failing score, and would otherwise pass silently.
    /// </remarks>
    public static void AssertNoDiagnosticErrors(EvaluationResult result)
    {
        var errors = result.Metrics.Values
            .SelectMany(metric => metric.Diagnostics ?? Array.Empty<EvaluationDiagnostic>())
            .Where(diagnostic => diagnostic.Severity is EvaluationDiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Message)
            .ToList();

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    /// <summary>
    /// Applies this suite's pass mark to the judged metrics, and leaves every other metric with
    /// the interpretation its own evaluator produced.
    /// </summary>
    /// <remarks>
    /// This hook is where a suite states its policy. The Quality library defaults to failing
    /// anything under 4.0 out of 5, which a small local model almost never clears — a suite that
    /// is always red is a suite nobody reads. Returning <see langword="null"/> for everything else
    /// matters just as much: the NLP evaluators and the safety evaluators already interpret their
    /// own scales, and overwriting those would replace a 0-to-7 severity or a 0-to-1 overlap score
    /// with a rating meant for a 1-to-5 rubric.
    /// </remarks>
    private static EvaluationMetricInterpretation? Interpret(EvaluationMetric metric)
    {
        if (metric is not NumericMetric judged || !JudgedMetricNames.Contains(metric.Name))
        {
            return null;
        }

        if (judged.Value is not double score)
        {
            return new EvaluationMetricInterpretation(
                EvaluationRating.Inconclusive,
                failed: false,
                reason: "The judge returned no score for this metric.");
        }

        var rating = score switch
        {
            >= 4.5 => EvaluationRating.Exceptional,
            >= 3.5 => EvaluationRating.Good,
            >= 2.5 => EvaluationRating.Average,
            >= 1.5 => EvaluationRating.Poor,
            _ => EvaluationRating.Unacceptable,
        };

        var floor = EvalEnvironment.QualityFloor;

        return score < floor
            ? new EvaluationMetricInterpretation(
                rating,
                failed: true,
                reason: string.Create(
                    CultureInfo.InvariantCulture,
                    $"{metric.Name} scored {score:0.##} against a floor of {floor:0.##}."))
            : new EvaluationMetricInterpretation(rating);
    }

    private static readonly HashSet<string> JudgedMetricNames = new(StringComparer.Ordinal)
    {
        RelevanceEvaluator.RelevanceMetricName,
        CoherenceEvaluator.CoherenceMetricName,
        GroundednessEvaluator.GroundednessMetricName,
        EquivalenceEvaluator.EquivalenceMetricName,
        TaskAdherenceEvaluator.TaskAdherenceMetricName,

        // Tool Call Accuracy is deliberately absent: it is a BooleanMetric, not a score out of
        // five, so the floor does not apply to it and its own pass/fail interpretation stands.
    };
}
