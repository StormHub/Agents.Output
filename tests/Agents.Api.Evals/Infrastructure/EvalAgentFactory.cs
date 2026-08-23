using Agents.Api.Options;
using Agents.Api.Tools;
using Agents.Evals.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agents.Api.Evals.Infrastructure;

/// <summary>
/// Builds the agent under evaluation, either scripted (offline, deterministic) or backed by a
/// real model.
/// </summary>
/// <remarks>
/// The scripted client, the canned tools and the agent's own instructions come from
/// <c>Agents.Evals.Infrastructure</c>, which the sibling <c>Agents.Extensions.Evals</c> suite also
/// builds on — the two suites evaluate different layers, but they have to evaluate the same agent
/// against the same tools or their results cannot be read together.
/// </remarks>
internal static class EvalAgentFactory
{
    /// <summary>
    /// Composes the agent exactly as production does — same name, instructions and tool shape —
    /// but over a scripted chat client and canned tools, so no model or network is involved.
    /// </summary>
    public static ChatClientAgent CreateScripted(ScriptedChatClient chatClient) =>
        chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = AgentContract.Name,
            ChatOptions = new ChatOptions
            {
                Instructions = AgentContract.Instructions,
                Tools = [.. StubWeatherTools.All()],
            },
        });

    /// <summary>
    /// Resolves the real agent through the production DI registrations, pointed at a live
    /// Ollama endpoint. Calls the model and Open-Meteo for real.
    /// </summary>
    public static ChatClientAgent CreateLive()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentChatOptions:Model"] = EvalEnvironment.Model,
                ["AgentChatOptions:BaseUrl"] = EvalEnvironment.BaseUrl,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTools();
        services.AddWeatherChatAgent(configuration);

        return services.BuildServiceProvider().GetRequiredService<ChatClientAgent>();
    }
}
