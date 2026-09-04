using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Html;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Json;

namespace Agents.Evals.Infrastructure.Probabilistic;

/// <summary>
/// One check's measured rate, as recorded in a report.
/// </summary>
/// <remarks>
/// <see cref="Rationale"/> and <see cref="Floor"/> are populated whenever the check has a
/// declared floor, whether it passed — a reader shouldn't have to wait for a failure to
/// find out why a check was chosen, or what floor it was judged against.
/// </remarks>
public sealed record CheckRateRecord(
    string Check,
    int Passed,
    int Total,
    double Observed,
    double LowerBound,
    double? Floor,
    bool? Gated,
    bool? IsInvariant,
    string? Rationale);

/// <summary>A single evaluation run, written to disk so runs can be compared over time.</summary>
/// <param name="Description">
/// Plain-language summary of what this scenario measures and why, so the report is readable on
/// its own without the test source.
/// </param>
/// <param name="Glossary">
/// Short explanations of the statistical terms used below (lower bound, invariant vs. rate
/// floor, gated vs. ungated), so a reader unfamiliar with the method can interpret the numbers.
/// </param>
public sealed record EvalRunRecord(
    string Scenario,
    string Description,
    DateTimeOffset TimestampUtc,
    string Model,
    int Items,
    IReadOnlyList<CheckRateRecord> Checks,
    IReadOnlyList<string> Violations,
    IReadOnlyList<string> Glossary);

/// <summary>
/// All scenarios measured so far in this test run, combined into a single gate-summary file.
/// </summary>
/// <param name="ReportGroup">The test file/class these scenarios belong to.</param>
/// <param name="RunId">
/// The short id shared by every file this process writes, so the gate-summary, <c>.eval.json</c>
/// and <c>.eval.html</c> for one run can be matched up by filename.
/// </param>
/// <param name="Scenarios">Every scenario measured so far, one entry each, most recent last.</param>
public sealed record EvalFileReport(
    string ReportGroup,
    string RunId,
    DateTimeOffset LastUpdatedUtc,
    IReadOnlyList<EvalRunRecord> Scenarios);

/// <summary>The report formats <see cref="EvalReport.WriteAsync"/> can emit.</summary>
[Flags]
public enum EvalReportFormat
{
    /// <summary>Our own lightweight per-check rate/floor/violation summary — used for the gate assertion.</summary>
    GateSummary = 1,

    /// <summary>
    /// The framework's per-item transcript report (<see cref="ScenarioRunResult"/>), written by
    /// <see cref="Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Json.JsonReportWriter"/>.
    /// </summary>
    Json = 2,

    /// <summary>
    /// The framework's per-item transcript report, written by
    /// <see cref="Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Html.HtmlReportWriter"/>.
    /// </summary>
    Html = 4,

    All = GateSummary | Json | Html,
}

/// <summary>
/// Writes evaluation runs to disk.
/// </summary>
/// <remarks>
/// A pass/fail verdict answers "should this build go red". It does not answer "is the agent
/// getting better or worse", which is the question evaluation exists for. Recording each run
/// keeps that answer available: the report is the deliverable, the gate is a side effect.
/// Set <see cref="EvaluationOptions.ReportDirectory"/> to control where reports land, and
/// <see cref="EvaluationOptions.ReportFormat"/> (<c>GateSummary</c>, <c>Json</c>, <c>Html</c>, or
/// <c>All</c> — the default) to control which of these get written.
/// </remarks>
/// <remarks>
/// A test file typically measures several scenarios (e.g. <c>LiveModelEvalTests</c> has three),
/// and a viewer shouldn't have to open a separate report per scenario to see the whole picture.
/// So reports are combined per <c>reportGroup</c> (normally the test class name): every scenario
/// measured during one process's lifetime accumulates into the same file, named with a short id
/// generated once when the process starts. A fresh <c>dotnet test</c> invocation gets a fresh id
/// and therefore a fresh file.
/// </remarks>
/// <remarks>
/// This class holds no state of its own between calls — each <see cref="WriteAsync"/> reads
/// whatever was already written for this run (if anything), merges in the scenario just
/// measured, and writes the combined result straight back out. That works because
/// <see cref="ScenarioRunResult"/> (and everything it references — <c>ChatMessage</c>,
/// <c>ChatResponse</c>, <c>EvaluationResult</c>) round-trips cleanly through
/// <see cref="System.Text.Json.JsonSerializer"/> with default options, and because the gate
/// summary is our own owned format. Only one test is expected to run at a time, so a
/// read-modify-write per call (no locking, no in-memory accumulator) is sufficient.
/// </remarks>
public static class EvalReport
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Identifies every file this process writes, so a fresh test run never collides with or
    /// silently overwrites-and-merges-with a previous run's report.
    /// </summary>
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Explains the statistical terms used throughout the report, so a reader unfamiliar with the
    /// method (e.g. someone triaging a failed build) doesn't have to go read the source or the
    /// README to understand what "lower bound" or "invariant" mean here.
    /// </summary>
    private static readonly IReadOnlyList<string> Glossary =
    [
        "observed rate: the raw pass rate seen in this sample (passed / total).",
        "95% lower bound: the low end of the 95% Wilson confidence interval for the true pass " +
            "rate. This — not the observed rate — is what's judged against a floor, because a " +
            "small perfect sample can still be an unlucky draw from a worse true rate.",
        "invariant (floor 100%): any failure is treated as a defect, not noise — no sampling " +
            "argument excuses it, so the check fails the moment one run misses.",
        "rate floor (below 100%): the agent is allowed to miss sometimes; the check only fails " +
            "if the lower bound drops below the floor, i.e. there's no longer 95% confidence " +
            "the true rate clears it.",
        "gated: this check has a floor and can fail the build. ungated: recorded for visibility " +
            "(e.g. the overall/joint pass rate) but never fails the build on its own.",
    ];

    /// <summary>
    /// Measures and writes one scenario's result, combined with every other scenario already
    /// measured for the same <paramref name="reportGroup"/> in this process, into a single file
    /// per format.
    /// </summary>
    /// <param name="reportGroup">
    /// Groups scenarios into one report — normally the test class name (e.g.
    /// <c>nameof(LiveModelEvalTests)</c>), so a whole test file's run lands in one file per
    /// format instead of one file per scenario.
    /// </param>
    public static async Task<IReadOnlyList<string>> WriteAsync(
        string reportGroup,
        string scenario,
        string description,
        GateOutcome outcome,
        AgentEvaluationResults results,
        IReadOnlyDictionary<string, CheckFloor> floors,
        string model,
        EvalReportFormat format = EvalReportFormat.All,
        CancellationToken cancellationToken = default)
    {
        var directory = EvalEnvironment.Current.ReportDirectory;
        Directory.CreateDirectory(directory);

        var stamp = DateTimeOffset.UtcNow;
        var baseName = $"{reportGroup}-{RunId}";
        var paths = new List<string>();

        var gateSummary = BuildGateSummary(scenario, description, stamp, outcome, results, floors, model);
        var scenarioResults = ToScenarioRunResults(scenario, description, model, stamp, outcome, results, floors);

        if (format.HasFlag(EvalReportFormat.GateSummary))
        {
            var gateSummaryPath = Path.Combine(directory, $"{baseName}.json");
            var combinedGateSummaries = await ReadExistingGateSummariesAsync(gateSummaryPath, cancellationToken);

            // A scenario that reruns within the same process replaces its previous entry rather
            // than duplicating it — the combined file reflects the latest measurement per scenario.
            combinedGateSummaries.RemoveAll(record => string.Equals(record.Scenario, scenario, StringComparison.Ordinal));
            combinedGateSummaries.Add(gateSummary);

            paths.Add(WriteGateSummary(gateSummaryPath, reportGroup, combinedGateSummaries));
        }

        if (format.HasFlag(EvalReportFormat.Json) || format.HasFlag(EvalReportFormat.Html))
        {
            // Both framework writers render the same combined transcript, so the accumulated
            // state is read back once (from the .eval.json this same code wrote) and reused for
            // both. This assumes the configured format stays constant for the life of the process —
            // true today since it's bound once into EvaluationOptions — so Json is always among the
            // formats whenever Html is, and the .eval.json this reads always reflects every
            // scenario measured so far.
            var jsonPath = Path.Combine(directory, $"{baseName}.eval.json");
            var htmlPath = Path.Combine(directory, $"{baseName}.eval.html");
            var combinedScenarioResults = await ReadExistingScenarioRunResultsAsync(jsonPath, cancellationToken);

            combinedScenarioResults.RemoveAll(result => string.Equals(result.ScenarioName, scenario, StringComparison.Ordinal));
            combinedScenarioResults.AddRange(scenarioResults);

            if (format.HasFlag(EvalReportFormat.Json))
            {
                await new JsonReportWriter(jsonPath).WriteReportAsync(combinedScenarioResults, cancellationToken);
                paths.Add(jsonPath);
            }

            if (format.HasFlag(EvalReportFormat.Html))
            {
                await new HtmlReportWriter(htmlPath).WriteReportAsync(combinedScenarioResults, cancellationToken);
                paths.Add(htmlPath);
            }
        }

        return paths;
    }

    /// <summary>
    /// Reads the gate-summary file already written for this run, if any — this is how the
    /// combined file accumulates scenarios across calls without any in-memory state: each call
    /// reads what's on disk, merges its own scenario in, and writes the whole thing back.
    /// </summary>
    private static async Task<List<EvalRunRecord>> ReadExistingGateSummariesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        var report = await JsonSerializer.DeserializeAsync<EvalFileReport>(stream, SerializerOptions, cancellationToken);
        return report is null ? [] : [.. report.Scenarios];
    }

    /// <summary>
    /// Reads the <see cref="ScenarioRunResult"/>s already written to the framework's own
    /// <c>.eval.json</c> for this run, if any, so they can be merged with the scenario just
    /// measured instead of being held in memory across calls. <see cref="JsonReportWriter"/>
    /// wraps its array in an envelope object (<c>{ scenarioRunResults, createdAt, ... }</c>) and
    /// exposes no reader of its own, so only the one property this code needs is pulled out.
    /// </summary>
    private static async Task<List<ScenarioRunResult>> ReadExistingScenarioRunResultsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("scenarioRunResults", out var element))
        {
            return [];
        }

        // JsonReportWriter serializes with camelCase property names; without matching that here,
        // constructor-parameter binding silently drops every field it can't case-sensitively
        // match instead of throwing, turning every previously-written scenario into a blank
        // record.
        return JsonSerializer.Deserialize<List<ScenarioRunResult>>(element.GetRawText(), ReadBackOptions) ?? [];
    }

    private static readonly JsonSerializerOptions ReadBackOptions = CreateReadBackOptions();

    /// <summary>
    /// Options for reading <see cref="JsonReportWriter"/>'s own output back in. Its internal
    /// serialization options aren't public, so this mirrors the two ways ours would otherwise
    /// diverge from it: camelCase property names (constructor-parameter binding fails silently,
    /// not loudly, on a case mismatch) and enums written as camelCase strings (e.g.
    /// <c>EvaluationRating.Good</c> as <c>"good"</c>), which the default enum converter can't
    /// read without a matching <see cref="JsonStringEnumConverter"/>.
    /// </summary>
    private static JsonSerializerOptions CreateReadBackOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static EvalRunRecord BuildGateSummary(
        string scenario,
        string description,
        DateTimeOffset stamp,
        GateOutcome outcome,
        AgentEvaluationResults results,
        IReadOnlyDictionary<string, CheckFloor> floors,
        string model) =>
        new(
            scenario,
            description,
            stamp,
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
                    gated,
                    gated ? floor!.IsInvariant : null,
                    gated ? floor!.Rationale : null);
            })],
            outcome.Violations,
            Glossary);

    private static string WriteGateSummary(
        string path,
        string reportGroup,
        IReadOnlyList<EvalRunRecord> scenarios)
    {
        var record = new EvalFileReport(reportGroup, RunId, DateTimeOffset.UtcNow, scenarios);
        File.WriteAllText(path, JsonSerializer.Serialize(record, SerializerOptions));

        return path;
    }

    /// <summary>
    /// Projects each evaluated item into a <see cref="ScenarioRunResult"/>, so the framework's
    /// own JSON/HTML writers can render the full transcript (messages, model response, per-item
    /// metrics) rather than just our aggregated rates.
    /// </summary>
    /// <remarks>
    /// The gate's own verdict has no equivalent field on <see cref="ScenarioRunResult"/> — it's a
    /// rate across the whole sample, not a per-item fact — so it is attached as <c>Tags</c>
    /// instead. The framework's HTML viewer shows one case per item and repeats every tag on
    /// every case, so attaching the full description/rates/glossary to <em>each</em> of possibly
    /// dozens of cases per scenario buries the transcript under duplicated boilerplate. Instead,
    /// only the first case (iteration 0) of each scenario carries the full summary; every other
    /// case gets a one-line pointer back to it, so opening the scenario's case list shows the
    /// summary once, at the top, the way a viewer would expect.
    /// </remarks>
    private static List<ScenarioRunResult> ToScenarioRunResults(
        string scenario,
        string description,
        string model,
        DateTimeOffset stamp,
        GateOutcome outcome,
        AgentEvaluationResults results,
        IReadOnlyDictionary<string, CheckFloor> floors)
    {
        List<string> summaryTags =
        [
            $"description: {description}",
            $"gate: {(outcome.Passed ? "passed" : "failed")}",
            .. outcome.Rates.Select(rate => DescribeRateWithRationale(rate, floors)),
            .. outcome.Violations.Select(violation => $"violation: {violation}"),
            .. Glossary.Select(entry => $"glossary: {entry}"),
        ];

        List<string> pointerTags =
        [
            "see case 0 in this scenario for the description, measured rates and glossary.",
        ];

        var scenarioResults = new List<ScenarioRunResult>(results.Items.Count);

        for (var i = 0; i < results.Items.Count; i++)
        {
            EvalItem? inputItem = null;
            var inputItems = results.InputItems;
            if (inputItems is not null && i < inputItems.Count)
            {
                inputItem = inputItems[i];
            }

            IReadOnlyList<ChatMessage> conversation = inputItem is null ? [] : inputItem.Conversation;
            var messages = conversation.ToList();
            var response = inputItem?.RawResponse ?? new ChatResponse();

            scenarioResults.Add(new ScenarioRunResult(
                scenario,
                iterationName: i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                executionName: model,
                stamp.UtcDateTime,
                messages,
                response,
                results.Items[i],
                chatDetails: null,
                tags: i == 0 ? summaryTags : pointerTags));
        }

        return scenarioResults;
    }

    /// <summary>
    /// Formats a measured rate together with the floor and rationale it's judged against, so the
    /// "why" travels with the number instead of only appearing when the check fails.
    /// </summary>
    private static string DescribeRateWithRationale(CheckRate rate, IReadOnlyDictionary<string, CheckFloor> floors)
    {
        if (!floors.TryGetValue(rate.CheckName, out var floor))
        {
            return $"rate: {rate.Describe()} — ungated, no floor declared.";
        }

        var kind = floor.IsInvariant ? "invariant" : $"floor {floor.Floor:P0}";
        return $"rate: {rate.Describe()} — {kind} — {floor.Rationale}";
    }
}
