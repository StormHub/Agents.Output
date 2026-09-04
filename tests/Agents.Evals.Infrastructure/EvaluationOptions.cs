using System.ComponentModel.DataAnnotations;
using Agents.Evals.Infrastructure.Probabilistic;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// Every knob both evaluation suites read, as one bound options object.
/// </summary>
/// <remarks>
/// <para>
/// The two suites answer different questions — <c>Trajectory</c> measures the Agent Framework
/// agent, <c>Metrics</c> measures the chat pipeline underneath it — but they are pointed at the
/// same deployment, the same endpoint and the same credential. A knob that means one
/// thing in one suite and something else in the other is a trap, so there is one definition of each
/// and one set of defaults.
/// </para>
/// <para>
/// This is a plain options class bound from an <c>IConfiguration</c> section the standard way, so
/// the environment is one <em>source</em> of these values rather than their definition. See
/// <see cref="EvaluationEnvironment"/> for the providers that feed it and the order they are
/// layered in.
/// </para>
/// <para>
/// The section is named for the type — <c>nameof(EvaluationOptions)</c>, the same way
/// <c>Agents.Api</c> binds its own <c>AgentChatOptions</c> — so an environment variable spells out
/// as <c>EvaluationOptions__Model</c> and a user secret as <c>EvaluationOptions:Model</c>. Naming
/// it from the type rather than a string constant means renaming the type renames the section with
/// it, and nothing can drift.
/// </para>
/// </remarks>
public sealed record EvaluationOptions
{
    /// <summary>
    /// Whether the tiers that call a real model are enabled. Set
    /// <c>EvaluationOptions__LiveModelEnabled=true</c> with a reachable endpoint and an API key to
    /// opt in; without it they skip, so CI stays offline and free.
    /// </summary>
    public bool LiveModelEnabled { get; init; }

    /// <summary>The deployment under evaluation. Recorded in reports so runs stay comparable.</summary>
    [Required]
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// The deployment that grades, where a suite uses a judge. Left unset it follows
    /// <see cref="Model"/>, which is the cheapest setup and the weakest one — a judge that shares
    /// the system's blind spots will not see them. Set <c>EvaluationOptions__JudgeModel</c> to
    /// something stronger once the scores start mattering.
    /// </summary>
    /// <remarks>
    /// Defaulted in <see cref="EvaluationEnvironment"/> after binding rather than in the defaults
    /// table, because it has to follow a <see cref="Model"/> the operator overrode, not the one
    /// shipped.
    /// </remarks>
    [Required]
    public string JudgeModel { get; init; } = string.Empty;

    /// <summary>Azure OpenAI endpoint serving both the system under test and the judge.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// Key for <see cref="BaseUrl"/>. The production registration rejects a blank key, so the live
    /// tiers of both suites cannot start without one.
    /// </summary>
    /// <remarks>
    /// Not <see cref="RequiredAttribute"/>: the offline tiers are the common case and the only case
    /// in CI, and they never call an endpoint. <see cref="EvaluationSetup.ForLiveModel"/> is where a
    /// missing key becomes an error, because that is the first point at which it matters.
    /// </remarks>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Runs per query where a suite gates on rates rather than on single runs.
    /// </summary>
    /// <remarks>
    /// Rate gating needs a real sample: the default of 35 is the smallest sample in which a
    /// flawless run clears a 90% floor on the lower bound of a 95% Wilson interval, with room to
    /// absorb the occasional miss on the 80% floors below it.
    /// </remarks>
    [Range(1, int.MaxValue)]
    public int SampleSize { get; init; }

    /// <summary>
    /// The score a judged metric has to clear, out of 5.
    /// </summary>
    /// <remarks>
    /// The Quality library's own default is 4.0. That is a defensible bar for a frontier model and
    /// an unreachable one for a modest deployment — every run would be red, and a suite that is
    /// always red stops being read. 3.0 is a starting point, to be moved once there is baseline data
    /// for the deployment actually in use.
    /// </remarks>
    [Range(0.0, 5.0)]
    public double QualityFloor { get; init; }

    /// <summary>
    /// Endpoint of the Azure AI Foundry project backing the content safety evaluators, e.g.
    /// <c>https://[account].services.ai.azure.com/api/projects/[project]</c>. Unset means the safety
    /// tier skips.
    /// </summary>
    [Url]
    public string? SafetyEndpoint { get; init; }

    /// <summary>Whether the safety tier has an endpoint to talk to.</summary>
    public bool SafetyEnabled => !string.IsNullOrWhiteSpace(SafetyEndpoint);

    /// <summary>Where the MEAI result store lives. Point <c>dotnet aieval report</c> at this.</summary>
    /// <remarks>
    /// The default sits beside the running test binary, under <c>bin/</c>, which is awkward to point
    /// a tool at — so every test prints the absolute path it used. Set
    /// <c>EvaluationOptions__StorageRoot</c> to somewhere stable to accumulate history, and to the
    /// same directory for both suites to gather them into one report.
    /// </remarks>
    [Required]
    public string StorageRoot { get; init; } = string.Empty;

    /// <summary>
    /// Names this run of the suite. Reports group and compare by execution, so set this to the CI
    /// build number to line runs up; it otherwise falls back to a timestamp.
    /// </summary>
    /// <remarks>
    /// Every scenario in one run has to share an execution name. The timestamp fallback is stamped
    /// once, when the defaults are built — recomputing it per read would scatter a single run across
    /// as many executions as there are scenarios, and the report would show no run at all, only
    /// fragments of one.
    /// </remarks>
    [Required]
    public string ExecutionName { get; init; } = string.Empty;

    /// <summary>
    /// How long a cached judge response stays valid. Long enough that re-running a red suite costs
    /// nothing, short enough that a redeployed model does not keep serving stale verdicts.
    /// </summary>
    public TimeSpan CacheTimeToLive { get; init; }

    /// <summary>Where <see cref="EvalReport"/> writes. Defaults beside the test binary.</summary>
    [Required]
    public string ReportDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Which report format(s) <see cref="EvalReport.WriteAsync"/> emits. Set
    /// <c>EvaluationOptions__ReportFormat</c> to <c>GateSummary</c>, <c>Json</c>, <c>Html</c>, or
    /// any comma-separated combination; defaults to <c>All</c>.
    /// </summary>
    /// <remarks>
    /// Binding this as the flags enum itself, rather than parsing a string by hand, is what makes a
    /// typo loud: the configuration binder rejects a value that names no member and says which key
    /// carried it. Silently ignoring one would produce a run that measured everything and recorded
    /// none of it.
    /// </remarks>
    public EvalReportFormat ReportFormat { get; init; }

    /// <summary>Identifies the run, without printing the credential.</summary>
    /// <remarks>
    /// A record's generated <c>ToString</c> renders every property, <see cref="ApiKey"/> included,
    /// and an options object is exactly the kind of thing that ends up in a failure message or a
    /// test log. This prints what identifies a run and says only whether the key is present.
    /// </remarks>
    public override string ToString() =>
        $"{nameof(EvaluationOptions)} {{ {nameof(Model)} = {Model}, {nameof(JudgeModel)} = {JudgeModel}, "
        + $"{nameof(BaseUrl)} = {BaseUrl}, {nameof(ApiKey)} = {(string.IsNullOrWhiteSpace(ApiKey) ? "(unset)" : "(set)")}, "
        + $"{nameof(LiveModelEnabled)} = {LiveModelEnabled}, {nameof(ExecutionName)} = {ExecutionName} }}";
}
