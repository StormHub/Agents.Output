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
}
