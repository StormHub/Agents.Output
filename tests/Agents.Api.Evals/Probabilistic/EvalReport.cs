using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;

namespace Agents.Api.Evals.Probabilistic;

/// <summary>One check's measured rate, as recorded in a report.</summary>
internal sealed record CheckRateRecord(
    string Check,
    int Passed,
    int Total,
    double Observed,
    double LowerBound,
    double? Floor,
    bool? Gated);

/// <summary>A single evaluation run, written to disk so runs can be compared over time.</summary>
internal sealed record EvalRunRecord(
    string Scenario,
    DateTimeOffset TimestampUtc,
    string Model,
    int Items,
    IReadOnlyList<CheckRateRecord> Checks,
    IReadOnlyList<string> Violations);

/// <summary>
/// Writes evaluation runs to disk.
/// </summary>
/// <remarks>
/// A pass/fail verdict answers "should this build go red". It does not answer "is the agent
/// getting better or worse", which is the question evaluation exists for. Recording each run
/// keeps that answer available: the report is the deliverable, the gate is a side effect.
/// Set <c>EVAL_REPORT_DIR</c> to control where reports land.
/// </remarks>
internal static class EvalReport
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write(
        string scenario,
        GateOutcome outcome,
        AgentEvaluationResults results,
        IReadOnlyDictionary<string, CheckFloor> floors,
        string model)
    {
        var directory = Environment.GetEnvironmentVariable("EVAL_REPORT_DIR")
                        ?? Path.Combine(AppContext.BaseDirectory, "eval-reports");
        Directory.CreateDirectory(directory);

        var record = new EvalRunRecord(
            scenario,
            DateTimeOffset.UtcNow,
            model,
            results.Total,
            [.. outcome.Rates.Select(rate =>
            {
                var gated = floors.TryGetValue(rate.CheckName, out var floor);
                return new CheckRateRecord(
                    rate.CheckName,
                    rate.Passed,
                    rate.Total,
                    Math.Round(rate.Observed, 4),
                    Math.Round(rate.LowerBound, 4),
                    gated ? floor!.Floor : null,
                    gated);
            })],
            outcome.Violations);

        var path = Path.Combine(
            directory,
            $"{scenario}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");

        File.WriteAllText(path, JsonSerializer.Serialize(record, SerializerOptions));

        return path;
    }
}
