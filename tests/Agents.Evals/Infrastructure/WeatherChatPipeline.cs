using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Agents.Evals.Infrastructure;

/// <summary>
/// The system under evaluation: a plain <see cref="IChatClient"/> pipeline built out of
/// <c>Microsoft.Extensions.AI</c> — the chat client, the function-invocation middleware, and the
/// tool contract the API exposes.
/// </summary>
/// <remarks>
/// <para>
/// This is not the Agent Framework agent that <c>Agents.Api</c> hosts, and it is not what
/// <c>Agents.Api.Evals</c> measures. It is the layer underneath: the instructions are production's
/// and the chat client is built by production's own DI registration, driven through MEAI's
/// abstractions, because that is the shape the <c>Microsoft.Extensions.AI.Evaluation</c> libraries
/// evaluate — they take <see cref="ChatMessage"/>s and a <see cref="ChatResponse"/> and know
/// nothing about agents.
/// </para>
/// <para>
/// The tools are the one deliberate substitution: <see cref="StubWeatherTools"/> stands in for the
/// Open-Meteo call so the readings are fixed and knowable, which is what gives the judged tiers a
/// ground truth to grade against.
/// </para>
/// </remarks>
internal static class WeatherChatPipeline
{
    /// <summary>
    /// The tool contract, shared by the pipeline and by the evaluators that grade tool use —
    /// <c>ToolCallAccuracyEvaluator</c> and <c>TaskAdherenceEvaluator</c> are handed these exact
    /// <see cref="AITool"/>s, so the judge sees the schema the model saw.
    /// </summary>
    public static IReadOnlyList<AITool> Tools { get; } = StubWeatherTools.All();

    /// <summary>Chat options matching the production agent: same instructions, same tool shape.</summary>
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

    /// <summary>The pipeline over the live deployment. Calls Azure OpenAI for real.</summary>
    public static IChatClient CreateLive() =>
        ResolveChatClient(EvalEnvironment.Model)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

    /// <summary>
    /// The deployment that grades. No function invocation: the judge is asked to score text, never
    /// to call tools.
    /// </summary>
    public static IChatClient CreateJudge() => ResolveChatClient(EvalEnvironment.JudgeModel);

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

    /// <summary>
    /// Pulls the chat client out from under the production agent registration.
    /// </summary>
    /// <remarks>
    /// <see cref="EvalServices"/> composes it exactly as <c>Agents.Api</c> does, so the suite
    /// measures the client the API builds — same transport, same options. Only the keyed
    /// <see cref="IChatClient"/> is taken, not the <c>ChatClientAgent</c> wrapped around it: this
    /// suite evaluates the layer beneath the agent, and the tools it registers are
    /// <see cref="StubWeatherTools"/> rather than production's.
    /// </remarks>
    private static IChatClient ResolveChatClient(string model) =>
        EvalServices.ForLiveModel(model).GetRequiredKeyedService<IChatClient>(model);
}
