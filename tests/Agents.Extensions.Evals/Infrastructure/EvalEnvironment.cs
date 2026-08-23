namespace Agents.Extensions.Evals.Infrastructure;

/// <summary>
/// The knobs this suite reads from the environment.
/// </summary>
/// <remarks>
/// The store and execution names deliberately use the same variables as the sibling
/// <c>Agents.Api.Evals</c> suite: both write <c>Microsoft.Extensions.AI.Evaluation.Reporting</c>
/// records, so pointing them at one <c>EVAL_STORE_DIR</c> puts both suites in a single
/// <c>dotnet aieval report</c>. Scenario names are prefixed per tier, so they never collide.
/// </remarks>
internal static class EvalEnvironment
{
    /// <summary>Where the result store lives. Point <c>dotnet aieval report</c> at this.</summary>
    /// <remarks>
    /// The default sits beside the test binary, which is awkward to point a tool at — every test
    /// prints the absolute path it used. Set <c>EVAL_STORE_DIR</c> to somewhere stable to
    /// accumulate history.
    /// </remarks>
    public static string StorageRoot =>
        Environment.GetEnvironmentVariable("EVAL_STORE_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "eval-store");

    /// <summary>
    /// Names this run of the suite. Reports group and compare by execution, so set this to the CI
    /// build number to line runs up; it otherwise falls back to a timestamp.
    /// </summary>
    /// <remarks>
    /// Resolved once per process. Every scenario in one run has to share an execution name —
    /// recomputing the timestamp per call would scatter a single run across as many executions as
    /// there are scenarios.
    /// </remarks>
    public static string ExecutionName => LazyExecutionName.Value;

    /// <summary>
    /// Whether the tiers that call a real model are enabled. Set <c>EVAL_LIVE_MODEL=1</c> with
    /// Ollama running to opt in; without it they skip, so CI stays offline.
    /// </summary>
    public static bool LiveModelEnabled =>
        Environment.GetEnvironmentVariable("EVAL_LIVE_MODEL") is "1" or "true";

    /// <summary>The model under evaluation. Recorded as a tag so runs stay comparable.</summary>
    public static string Model =>
        Environment.GetEnvironmentVariable("EVAL_OLLAMA_MODEL") ?? "qwen3.5";

    /// <summary>
    /// The model that grades. Defaults to the model under evaluation, which is the cheapest
    /// setup and the weakest one — a judge that shares the system's blind spots will not see
    /// them. Set <c>EVAL_JUDGE_MODEL</c> to something stronger when the scores start mattering.
    /// </summary>
    public static string JudgeModel =>
        Environment.GetEnvironmentVariable("EVAL_JUDGE_MODEL") ?? Model;

    /// <summary>Ollama endpoint serving both the system under test and the judge.</summary>
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("EVAL_OLLAMA_BASEURL") ?? "http://localhost:11434";

    /// <summary>
    /// The score a judged metric has to clear, out of 5.
    /// </summary>
    /// <remarks>
    /// The library's own default is 4.0, which a small local model rarely clears — every run
    /// would be red and the suite would stop being read. 3.0 is a starting point to be moved once
    /// there is baseline data for the model in use.
    /// </remarks>
    public static double QualityFloor =>
        double.TryParse(Environment.GetEnvironmentVariable("EVAL_QUALITY_FLOOR"), out var floor)
            ? floor
            : 3.0;

    /// <summary>
    /// Endpoint of the Azure AI Foundry project backing the content safety evaluators, e.g.
    /// <c>https://[account].services.ai.azure.com/api/projects/[project]</c>. Unset means the
    /// safety tier skips.
    /// </summary>
    public static string? SafetyEndpoint => Environment.GetEnvironmentVariable("EVAL_SAFETY_ENDPOINT");

    /// <summary>Whether the safety tier has an endpoint to talk to.</summary>
    public static bool SafetyEnabled => !string.IsNullOrWhiteSpace(SafetyEndpoint);

    /// <summary>
    /// How long a cached judge response stays valid. Long enough that re-running a red suite
    /// costs nothing, short enough that a model upgrade does not keep serving stale verdicts.
    /// </summary>
    public static TimeSpan CacheTimeToLive => TimeSpan.FromDays(14);

    private static readonly Lazy<string> LazyExecutionName = new(() =>
        Environment.GetEnvironmentVariable("EVAL_EXECUTION_NAME")
        ?? $"local-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
}
