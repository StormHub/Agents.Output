using System.Globalization;
using Agents.Evals.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;

namespace Agents.Api.Evals.Probabilistic;

/// <summary>
/// Persists agent evaluation results into the `Microsoft.Extensions.AI.Evaluation.Reporting`
/// store, so runs accumulate a history and `dotnet aieval report` can render them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the store is written directly.</b> The idiomatic path is
/// <c>ReportingConfiguration</c> → <c>ScenarioRun.EvaluateAsync</c> → dispose, which persists for
/// you. That path is closed here: <c>ReportingConfiguration.Evaluators</c> takes MEAI's
/// <c>IEvaluator</c>, which scores one item at a time, while Agent Framework's evaluators
/// implement <c>IAgentEvaluator</c>, which scores a batch. The framework ships an adapter from
/// <c>IEvaluator</c> to <c>IAgentEvaluator</c> and nothing in the other direction, so
/// <c>LocalEvaluator</c> cannot be handed to a <c>ReportingConfiguration</c>.
/// </para>
/// <para>
/// The two libraries do meet at MEAI's <c>EvaluationResult</c>:
/// <c>AgentEvaluationResults.Items</c> is a list of them, and <c>ScenarioRunResult</c> has a
/// public constructor that takes one. So results are mapped and written to the store directly,
/// bypassing <c>ScenarioRun</c>. The stored records are ordinary ones — the report tool cannot
/// tell how they got there.
/// </para>
/// <para>
/// <b>The hierarchy lines up.</b> The store is organised as execution → scenario → iteration,
/// which is exactly how the suite already runs: one execution per suite run, one scenario per
/// query, and one iteration per repetition. <c>numRepetitions: 30</c> becomes 30 iterations of a
/// scenario, which is the shape the report was built to display.
/// </para>
/// </remarks>
internal static class EvalResultStore
{
    /// <summary>
    /// Writes one <see cref="ScenarioRunResult"/> per evaluated item.
    /// </summary>
    /// <returns>The storage root the results were written to.</returns>
    public static async Task<string> WriteAsync(
        string evalName,
        AgentEvaluationResults results,
        string model,
        CancellationToken cancellationToken = default)
    {
        var inputItems = results.InputItems;
        if (inputItems is null || inputItems.Count != results.Items.Count)
        {
            // Without the originating items there is no conversation to record, and a
            // ScenarioRunResult without one is not worth storing.
            throw new InvalidOperationException(
                $"{nameof(AgentEvaluationResults)}.{nameof(AgentEvaluationResults.InputItems)} does not line up "
                + $"with {nameof(AgentEvaluationResults.Items)}; cannot record this run.");
        }

        var executionName = EvalEnvironment.ExecutionName;
        var createdAt = DateTime.UtcNow;

        // Scenario is keyed off the query text rather than the item's position, so the mapping
        // does not depend on the order EvaluateAsync happens to emit repetitions in.
        var queryOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var iterationCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var records = new List<ScenarioRunResult>(results.Items.Count);

        for (var i = 0; i < results.Items.Count; i++)
        {
            var item = inputItems[i];

            if (!queryOrdinals.TryGetValue(item.Query, out var ordinal))
            {
                ordinal = queryOrdinals.Count;
                queryOrdinals[item.Query] = ordinal;
            }

            var scenarioName = $"{evalName}.q{ordinal:D2}";
            var iteration = iterationCounts.GetValueOrDefault(scenarioName) + 1;
            iterationCounts[scenarioName] = iteration;

            var (queryMessages, responseMessages) = item.Split();

            // Prefer the full response trajectory over EvalItem.RawResponse, which the framework
            // populates with the last message only — the tool calls are the interesting part of
            // this agent's behaviour and belong in the report.
            var modelResponse = responseMessages.Count > 0
                ? new ChatResponse([.. responseMessages])
                : item.RawResponse ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, item.Response));

            records.Add(new ScenarioRunResult(
                scenarioName,
                iteration.ToString("D3", CultureInfo.InvariantCulture),
                executionName,
                createdAt,
                queryMessages,
                modelResponse,
                results.Items[i],
                null,
                new[] { $"model:{model}", $"eval:{evalName}" }));
        }

        var store = new DiskBasedResultStore(EvalEnvironment.StorageRoot);
        await store.WriteResultsAsync(records, cancellationToken).ConfigureAwait(false);

        return EvalEnvironment.StorageRoot;
    }
}
