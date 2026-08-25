using Agents.Api.Options;
using Agents.Api.Tools;
using Agents.Api.Evals.Probabilistic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptionsRegistration = Agents.Api.Options.DependencyInjection;

namespace Agents.Api.Evals.Infrastructure;

/// <summary>
/// Builds the agent under evaluation, either scripted (offline, deterministic) or backed by a
/// real model.
/// </summary>
internal static class EvalAgentFactory
{
    /// <summary>
    /// Composes the agent exactly as production does — same name, instructions and tool shape —
    /// but over a scripted chat client and canned tools, so no model or network is involved.
    /// </summary>
    public static ChatClientAgent CreateScripted(ScriptedChatClient chatClient) =>
        chatClient.AsAIAgent(
            new ChatClientAgentOptions
            {
                Name = OptionsRegistration.AgentName,
                ChatOptions = new ChatOptions
                {
                    Instructions = OptionsRegistration.AgentInstructions,
                    Tools = [
                        StubWeatherTools.Calendar(),
                        StubWeatherTools.Weather()
                    ],
            },
        });

    /// <summary>
    /// Live-eval configuration, layered lowest → highest precedence: in-memory defaults (today's
    /// hardcoded fallbacks, unchanged), User Secrets (local-only, opt-in via
    /// <c>dotnet user-secrets set &lt;KEY&gt; &lt;VALUE&gt;</c> — scaffolding for a future secret
    /// such as a hosted-model API key that shouldn't live in shell env or source control), then
    /// environment variables (still wins, so CI/scripted invocations behave exactly as before).
    /// </summary>
    /// <remarks>
    /// <see cref="ConfigurationExtensions.AddUserSecrets(IConfigurationBuilder, System.Reflection.Assembly, bool, bool)"/>
    /// is called with <c>optional: true</c> so a machine that has never run
    /// <c>dotnet user-secrets set</c> for this project — the common case — never throws building
    /// this. The scripted/offline suite (<see cref="CreateScripted"/>) never touches this
    /// configuration at all, so a missing or malformed secrets file can't affect it.
    /// </remarks>
    private static readonly Lazy<IConfiguration> LiveConfiguration = new(() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EVAL_SAMPLE_SIZE"] = "35",
                    ["EVAL_REPORT_FORMAT"] = "all",
                })
            .AddUserSecrets(typeof(EvalAgentFactory).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build());

    /// <summary>
    /// Resolves the real agent through the production DI registrations, pointed at a live
    /// Ollama endpoint. Calls the model and Open-Meteo for real.
    /// </summary>
    public static ChatClientAgent CreateLive()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AgentChatOptions:Model"] = LiveModel,
                    ["AgentChatOptions:BaseUrl"] = LiveBaseUrl,
                    ["AgentChatOptions:ApiKey"] = LiveApiKey,
                })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTools();
        services.AddWeatherChatAgent(configuration);

        return services.BuildServiceProvider().GetRequiredService<ChatClientAgent>();
    }

    /// <summary>
    /// Whether live-model evaluation is enabled. Set <c>EVAL_LIVE_MODEL=1</c> with Ollama
    /// running to opt in; without it the live suite is skipped so CI stays offline.
    /// </summary>
    public static bool LiveModelEnabled =>
        LiveConfiguration.Value["EVAL_LIVE_MODEL"] is "1" or "true";

    /// <summary>The model under evaluation. Recorded in reports so runs stay comparable.</summary>
    public static string LiveModel =>
        LiveConfiguration.Value["EVAL_MODEL"] ?? "gpt-4.1-dz-1";

    /// <summary>
    /// Runs per query in the live suite. Rate gating needs a real sample — the default of 35 is
    /// enough for a flawless run to clear the highest floor in use (90%, <c>answered</c>) with
    /// room to absorb the occasional miss on the lower 80% floors.
    /// </summary>
    public static int SampleSize =>
        int.TryParse(LiveConfiguration.Value["EVAL_SAMPLE_SIZE"], out var size) && size > 0
            ? size
            : 35;

    private static string LiveBaseUrl =>
        LiveConfiguration.Value["EVAL_BASEURL"] ?? "http://localhost:11434";

    /// <summary>
    /// Which report format(s) <c>EvalReport.WriteAsync</c> emits. Set <c>EVAL_REPORT_FORMAT</c>
    /// to <c>gate-summary</c>, <c>json</c>, <c>html</c>, or any comma-separated combination;
    /// defaults to <c>all</c> (every format).
    /// </summary>
    public static EvalReportFormat ReportFormat
    {
        get
        {
            var raw = LiveConfiguration.Value["EVAL_REPORT_FORMAT"] ?? "all";
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var format = default(EvalReportFormat);
            foreach (var part in parts)
            {
                format |= part.ToLowerInvariant() switch
                {
                    "all" => EvalReportFormat.All,
                    "gate-summary" => EvalReportFormat.GateSummary,
                    "json" => EvalReportFormat.Json,
                    "html" => EvalReportFormat.Html,
                    _ => throw new InvalidOperationException(
                        $"Unrecognised EVAL_REPORT_FORMAT value '{part}'. Expected one or more of: gate-summary, json, html, all."),
                };
            }

            return format == default ? EvalReportFormat.All : format;
        }
    }

    /// <summary>
    /// Scaffolding for a future secret: an API key for a hosted model, set locally via
    /// <c>dotnet user-secrets set EVAL_API_KEY "..."</c> so it never touches shell env or source
    /// control. Unset today — Ollama needs none — so <see cref="CreateLive"/>'s behavior is
    /// unchanged while this stays null.
    /// </summary>
    private static string? LiveApiKey =>
        LiveConfiguration.Value["EVAL_API_KEY"];
}
