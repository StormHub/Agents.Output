using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Agents.Extensions.Evals.Infrastructure;

/// <summary>
/// An <see cref="IChatClient"/> that replays a fixed script instead of calling a model, so the
/// offline tier runs deterministically and without a network.
/// </summary>
/// <remarks>
/// The client is stateless: it decides what to emit by inspecting the conversation it is handed
/// rather than by counting calls, so concurrent or repeated runs of the same query cannot
/// interleave. For each request it finds the scenario matching the first user message, emits the
/// first tool call that is not already present in the conversation, and falls through to the
/// scripted answer once every scripted tool has been called. That is exactly the loop
/// <see cref="FunctionInvokingChatClient"/> drives, so the pipeline under evaluation is the real
/// one — only the model is fake.
/// </remarks>
internal sealed class ScriptedChatClient(params WeatherScenario[] scenarios) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var conversation = messages as IReadOnlyList<ChatMessage> ?? messages.ToList();
        var scenario = this.MatchScenario(conversation);

        var alreadyCalled = conversation
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(call => call.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var next = scenario.ToolCalls.FirstOrDefault(call => !alreadyCalled.Contains(call.Name));
        if (next is not null)
        {
            var content = new FunctionCallContent(
                $"call-{alreadyCalled.Count + 1}",
                next.Name,
                next.Arguments);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [content])));
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, scenario.ScriptedAnswer)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await this.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        // Nothing to release.
    }

    private WeatherScenario MatchScenario(IReadOnlyList<ChatMessage> conversation)
    {
        var query = conversation.FirstOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;

        return scenarios.FirstOrDefault(scenario => string.Equals(scenario.Query, query, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"No scripted scenario matches the query \"{query}\".");
    }
}
