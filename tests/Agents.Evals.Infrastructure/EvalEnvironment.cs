using Microsoft.Extensions.Configuration;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// Every knob both evaluation suites read from their environment, in one place.
/// </summary>
/// <remarks>
/// <para>
/// The two suites answer different questions — <c>Agents.Api.Evals</c> measures the Agent Framework
/// agent, <c>Agents.Extensions.Evals</c> measures the chat pipeline underneath it — but they are
/// pointed at the same deployment, the same endpoint and the same credential. A knob that means one
/// thing in one suite and something else in the other is a trap, so there is one definition of each
/// and one set of defaults.
/// </para>
/// <para>
/// Values are layered lowest → highest precedence: in-memory defaults, then User Secrets
/// (local-only, opt-in via <c>dotnet user-secrets set &lt;KEY&gt; &lt;VALUE&gt;</c>), then
/// environment variables. The endpoint this repository points at is Azure OpenAI, which needs an
/// API key, and a key does not belong in shell history or in source control — User Secrets is where
/// it goes. Environment variables still win, so CI and scripted invocations behave the way an
/// operator expects.
/// </para>
/// <para>
/// Because the secrets store belongs to <em>this</em> assembly, one
/// <c>dotnet user-secrets set --project tests/Agents.Evals.Infrastructure</c> configures both
/// suites rather than each needing its own copy of the same key.
/// </para>
/// </remarks>
public static class EvalEnvironment
{
    /// <summary>
    /// The default deployment and endpoint, matching <c>Agents.Api</c>'s own
    /// <c>appsettings.json</c>, so an unconfigured run measures the deployment the API actually
    /// uses.
    /// </summary>
    private const string DefaultModel = "gpt-4.1-dz-1";

    private const string DefaultBaseUrl = "https://shared-openai.openai.azure.com";

    /// <remarks>
    /// <see cref="ConfigurationExtensions.AddUserSecrets(IConfigurationBuilder, System.Reflection.Assembly, bool, bool)"/>
    /// is called with <c>optional: true</c> so a machine that has never run
    /// <c>dotnet user-secrets set</c> for this project — the common case, and the only case in CI —
    /// never throws building this. The offline tiers of both suites read nothing from here but the
    /// report and store locations, so a missing or malformed secrets file cannot affect them.
    /// </remarks>
    private static readonly Lazy<IConfiguration> Configuration = new(() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EVAL_MODEL"] = DefaultModel,
                    ["EVAL_BASEURL"] = DefaultBaseUrl,
                    ["EVAL_SAMPLE_SIZE"] = "35",
                    ["EVAL_QUALITY_FLOOR"] = "3.0",
                })
            .AddUserSecrets(typeof(EvalEnvironment).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build());

    /// <summary>
    /// Reads a knob that belongs to a single suite, through the same layering as everything else.
    /// </summary>
    /// <remarks>
    /// Not every setting is shared — <c>EVAL_REPORT_DIR</c> and <c>EVAL_REPORT_FORMAT</c> only mean
    /// something to <c>Agents.Api.Evals</c>' own report writer. Those stay defined in the suite that
    /// owns them, but still resolve through this configuration, so "environment variable beats User
    /// Secrets beats default" holds for every knob rather than only the shared ones.
    /// </remarks>
    public static string? Setting(string key) => Configuration.Value[key];

    /// <summary>
    /// Whether the tiers that call a real model are enabled. Set <c>EVAL_LIVE_MODEL=1</c> with a
    /// reachable endpoint and an API key to opt in; without it they skip, so CI stays offline and
    /// free.
    /// </summary>
    public static bool LiveModelEnabled => Configuration.Value["EVAL_LIVE_MODEL"] is "1" or "true";

    /// <summary>The deployment under evaluation. Recorded in reports so runs stay comparable.</summary>
    public static string Model => Configuration.Value["EVAL_MODEL"] ?? DefaultModel;

    /// <summary>
    /// The deployment that grades, where a suite uses a judge. Defaults to the deployment under
    /// evaluation, which is the cheapest setup and the weakest one — a judge that shares the
    /// system's blind spots will not see them. Set <c>EVAL_JUDGE_MODEL</c> to something stronger
    /// once the scores start mattering.
    /// </summary>
    public static string JudgeModel => Configuration.Value["EVAL_JUDGE_MODEL"] ?? Model;

    /// <summary>Azure OpenAI endpoint serving both the system under test and the judge.</summary>
    public static string BaseUrl => Configuration.Value["EVAL_BASEURL"] ?? DefaultBaseUrl;

    /// <summary>
    /// Key for <see cref="BaseUrl"/>. The production registration rejects a blank key, so the live
    /// tiers of both suites cannot start without one.
    /// </summary>
    public static string? ApiKey => Configuration.Value["EVAL_API_KEY"];

    /// <summary>
    /// Runs per query where a suite gates on rates rather than on single runs.
    /// </summary>
    /// <remarks>
    /// Rate gating needs a real sample: the default of 35 is the smallest sample in which a
    /// flawless run clears a 90% floor on the lower bound of a 95% Wilson interval, with room to
    /// absorb the occasional miss on the 80% floors below it.
    /// </remarks>
    public static int SampleSize =>
        int.TryParse(Configuration.Value["EVAL_SAMPLE_SIZE"], out var size) && size > 0 ? size : 35;

    /// <summary>
    /// The score a judged metric has to clear, out of 5.
    /// </summary>
    /// <remarks>
    /// The Quality library's own default is 4.0. That is a defensible bar for a frontier model and
    /// an unreachable one for a modest deployment — every run would be red, and a suite that is
    /// always red stops being read. 3.0 is a starting point, to be moved once there is baseline data
    /// for the deployment actually in use.
    /// </remarks>
    public static double QualityFloor =>
        double.TryParse(Configuration.Value["EVAL_QUALITY_FLOOR"], out var floor) ? floor : 3.0;

    /// <summary>
    /// Endpoint of the Azure AI Foundry project backing the content safety evaluators, e.g.
    /// <c>https://[account].services.ai.azure.com/api/projects/[project]</c>. Unset means the safety
    /// tier skips.
    /// </summary>
    public static string? SafetyEndpoint => Configuration.Value["EVAL_SAFETY_ENDPOINT"];

    /// <summary>Whether the safety tier has an endpoint to talk to.</summary>
    public static bool SafetyEnabled => !string.IsNullOrWhiteSpace(SafetyEndpoint);

    /// <summary>Where the MEAI result store lives. Point <c>dotnet aieval report</c> at this.</summary>
    /// <remarks>
    /// The default sits beside the running test binary, under <c>bin/</c>, which is awkward to point
    /// a tool at — so every test prints the absolute path it used. Set <c>EVAL_STORE_DIR</c> to
    /// somewhere stable to accumulate history, and to the same directory for both suites to gather
    /// them into one report.
    /// </remarks>
    public static string StorageRoot =>
        Configuration.Value["EVAL_STORE_DIR"] ?? Path.Combine(AppContext.BaseDirectory, "eval-store");

    /// <summary>
    /// Names this run of the suite. Reports group and compare by execution, so set this to the CI
    /// build number to line runs up; it otherwise falls back to a timestamp.
    /// </summary>
    /// <remarks>
    /// Resolved once per process. Every scenario in one run has to share an execution name —
    /// recomputing the timestamp per call would scatter a single run across as many executions as
    /// there are scenarios, and the report would show no run at all, only fragments of one.
    /// </remarks>
    public static string ExecutionName => LazyExecutionName.Value;

    /// <summary>
    /// How long a cached judge response stays valid. Long enough that re-running a red suite costs
    /// nothing, short enough that a redeployed model does not keep serving stale verdicts.
    /// </summary>
    public static TimeSpan CacheTimeToLive => TimeSpan.FromDays(14);

    private static readonly Lazy<string> LazyExecutionName = new(() =>
        Configuration.Value["EVAL_EXECUTION_NAME"] ?? $"local-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
}
