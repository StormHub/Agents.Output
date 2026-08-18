using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Agents.Api.Evals.Probabilistic;

/// <summary>
/// Turns a batch of evaluation results into per-check pass rates.
/// </summary>
/// <remarks>
/// <see cref="AgentEvaluationResults"/> exposes only <c>Passed</c>, <c>Failed</c>, <c>Total</c>
/// and <c>AllPassed</c> — an item counts as failed if any single check failed, so one flaky
/// check hides every other result. Splitting the batch per check is what makes it possible to
/// hold a safety invariant at 100% while letting a routing check sit at 90%.
/// </remarks>
internal static class EvalRates
{
    /// <summary>
    /// Computes the pass rate of every check across the sample, ordered by check name.
    /// </summary>
    public static IReadOnlyList<CheckRate> PerCheck(AgentEvaluationResults results)
    {
        var passed = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in results.Items)
        {
            foreach (var (name, metric) in item.Metrics)
            {
                total[name] = total.GetValueOrDefault(name) + 1;
                passed[name] = passed.GetValueOrDefault(name) + (MetricPassed(metric) ? 1 : 0);
            }
        }

        return total.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new CheckRate(name, passed[name], total[name]))
            .ToList();
    }

    /// <summary>
    /// The rate of runs in which every check passed. Falls to the product of the individual
    /// rates when checks fail independently, which is why it is reported but not gated on.
    /// </summary>
    public static CheckRate Overall(AgentEvaluationResults results) =>
        new("all_checks", results.Passed, results.Total);

    /// <summary>
    /// Mirrors how <see cref="AgentEvaluationResults"/> decides an item passed, applied to a
    /// single metric: trust the evaluator's own interpretation first, then an explicit false.
    /// </summary>
    private static bool MetricPassed(EvaluationMetric metric)
    {
        if (metric.Interpretation?.Failed == true)
        {
            return false;
        }

        return metric is not BooleanMetric { Value: false };
    }
}
