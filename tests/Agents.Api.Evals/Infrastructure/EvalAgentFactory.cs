using Agents.Api.Options;
using Agents.Api.Tools;
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
        chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = OptionsRegistration.AgentName,
            ChatOptions = new ChatOptions
            {
                Instructions = OptionsRegistration.AgentInstructions,
                Tools = [StubWeatherTools.Calendar(), StubWeatherTools.Weather()],
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
                ["AgentChatOptions:Model"] = LiveModel,
                ["AgentChatOptions:BaseUrl"] = LiveBaseUrl,
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
        Environment.GetEnvironmentVariable("EVAL_LIVE_MODEL") is "1" or "true";

    /// <summary>The model under evaluation. Recorded in reports so runs stay comparable.</summary>
    public static string LiveModel =>
        Environment.GetEnvironmentVariable("EVAL_OLLAMA_MODEL") ?? "qwen3.5";

    /// <summary>
    /// Runs per query in the live suite. Rate gating needs a real sample — the default of 30 is
    /// enough for a flawless run to clear an 80% floor with room to absorb one miss.
    /// </summary>
    public static int SampleSize =>
        int.TryParse(Environment.GetEnvironmentVariable("EVAL_SAMPLE_SIZE"), out var size) && size > 0
            ? size
            : 30;

    private static string LiveBaseUrl =>
        Environment.GetEnvironmentVariable("EVAL_OLLAMA_BASEURL") ?? "http://localhost:11434";
}
