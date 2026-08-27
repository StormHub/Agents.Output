using Agents.Api.Evals.Probabilistic;
using Agents.Evals.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Agents.Api.Evals.Infrastructure;

/// <summary>
/// Builds the agent under evaluation, either scripted (offline, deterministic) or backed by a real
/// model.
/// </summary>
/// <remarks>
/// The fixtures and the endpoint configuration live in <c>Agents.Evals.Infrastructure</c>, shared
/// with <c>Agents.Extensions.Evals</c>. What stays here is the one thing that is specific to this
/// suite: producing a <see cref="ChatClientAgent"/> — the Agent Framework object this suite
/// measures — rather than the <c>IChatClient</c> underneath it.
/// </remarks>
internal static class EvalAgentFactory
{
    /// <summary>
    /// Composes the agent exactly as production does — same name, instructions and tool shape — but
    /// over a scripted chat client and canned tools, so no model or network is involved.
    /// </summary>
    public static ChatClientAgent CreateScripted(ScriptedChatClient chatClient) =>
        chatClient.AsAIAgent(
            new ChatClientAgentOptions
            {
                Name = AgentContract.Name,
                ChatOptions = new ChatOptions
                {
                    Instructions = AgentContract.Instructions,
                    Tools = [.. StubWeatherTools.All()],
                },
            });

    /// <summary>
    /// Resolves the real agent through the production DI registrations. Calls the model and
    /// Open-Meteo for real.
    /// </summary>
    /// <remarks>
    /// Unlike the sibling suite this asks for the production tools, because what it measures is
    /// whether the agent routes to them correctly — a stub would make the tool call succeed for
    /// reasons the API would not enjoy.
    /// </remarks>
    public static ChatClientAgent CreateLive() =>
        EvalServices
            .ForLiveModel(EvalEnvironment.Model, withProductionTools: true)
            .GetRequiredService<ChatClientAgent>();

    /// <summary>
    /// Which report format(s) <see cref="EvalReport.WriteAsync"/> emits. Set
    /// <c>EVAL_REPORT_FORMAT</c> to <c>gate-summary</c>, <c>json</c>, <c>html</c>, or any
    /// comma-separated combination; defaults to <c>all</c> (every format).
    /// </summary>
    /// <remarks>
    /// Read through <see cref="EvalEnvironment.Setting"/> rather than straight off the environment,
    /// so this suite-specific knob still layers the same way as the shared ones. It stays defined
    /// here because <see cref="EvalReportFormat"/> is this suite's own type — the sibling suite has
    /// no report writer for it to mean anything to.
    /// </remarks>
    public static EvalReportFormat ReportFormat
    {
        get
        {
            var raw = EvalEnvironment.Setting("EVAL_REPORT_FORMAT") ?? "all";
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
}
