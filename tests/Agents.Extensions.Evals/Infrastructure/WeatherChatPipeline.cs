using Agents.Evals.Infrastructure;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace Agents.Extensions.Evals.Infrastructure;

/// <summary>
/// The system under evaluation: a plain <see cref="IChatClient"/> pipeline built out of
/// <c>Microsoft.Extensions.AI</c> — the chat client, the function-invocation middleware, and the
/// tool contract the API exposes.
/// </summary>
/// <remarks>
/// This is not the Agent Framework agent that <c>Agents.Api</c> hosts, and it is not what
/// <c>Agents.Api.Evals</c> measures. It is the layer underneath: the instructions and tools are
/// production's, driven through MEAI's own abstractions, because that is the shape the
/// <c>Microsoft.Extensions.AI.Evaluation</c> libraries evaluate — they take
/// <see cref="ChatMessage"/>s and a <see cref="ChatResponse"/> and know nothing about agents.
/// </remarks>
internal static class WeatherChatPipeline
{
    /// <summary>
    /// The tool contract, shared by the pipeline and by the evaluators that grade tool use —
    /// <c>ToolCallAccuracyEvaluator</c> and <c>TaskAdherenceEvaluator</c> are handed these exact
    /// <see cref="AITool"/>s, so the judge sees the schema the model saw.
    /// </summary>
    public static IReadOnlyList<AITool> Tools { get; } = StubWeatherTools.All();

    /// <summary>Chat options matching the production agent: same instructions, same tools.</summary>
    public static ChatOptions CreateOptions() =>
        new()
        {
            Instructions = AgentContract.Instructions,
            Tools = [.. Tools],
        };

    /// <summary>The pipeline over a scripted model: deterministic, offline, no model call.</summary>
    public static IChatClient CreateScripted(params WeatherScenario[] scenarios) =>
        new ScriptedChatClient(scenarios)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

    /// <summary>The pipeline over a live model. Calls Ollama for real.</summary>
    public static IChatClient CreateLive() =>
        CreateOllamaClient(EvalEnvironment.Model)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

    /// <summary>
    /// The model that grades. No function invocation: the judge is asked to score text, never to
    /// call tools.
    /// </summary>
    public static IChatClient CreateJudge() => CreateOllamaClient(EvalEnvironment.JudgeModel);

    /// <summary>
    /// Runs one query and returns both halves an evaluator needs: the conversation that produced
    /// the response, and the response itself.
    /// </summary>
    public static async Task<(IList<ChatMessage> Messages, ChatResponse Response)> RunAsync(
        IChatClient client,
        string query,
        CancellationToken cancellationToken)
    {
        List<ChatMessage> messages = [new ChatMessage(ChatRole.User, query)];

        var response = await client.GetResponseAsync(messages, CreateOptions(), cancellationToken)
            .ConfigureAwait(false);

        return (messages, response);
    }

    private static IChatClient CreateOllamaClient(string model)
    {
        // A local model working through a tool loop is slow; the default 100s timeout is not
        // enough to tell "still thinking" from "wedged".
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(EvalEnvironment.BaseUrl),
            Timeout = TimeSpan.FromMinutes(5),
        };

        return new OllamaApiClient(httpClient, model);
    }
}
