namespace Agents.Api.Evals.Infrastructure;

/// <summary>
/// A single tool call the scripted chat client should emit.
/// </summary>
/// <param name="Name">Tool name, matching the name the agent registers.</param>
/// <param name="Arguments">Arguments to emit, or <c>null</c> for a no-argument tool.</param>
internal sealed record ScriptedToolCall(string Name, IDictionary<string, object?>? Arguments = null);

/// <summary>
/// A scripted agent turn: the query, the tool calls the model should make in order, and the
/// final text it should produce once every tool has returned.
/// </summary>
/// <remarks>
/// Each tool may appear at most once per scenario — <see cref="ScriptedChatClient"/> tracks
/// progress by tool name, so a scenario that calls the same tool twice would loop.
/// </remarks>
/// <param name="Query">The user query. Matched verbatim.</param>
/// <param name="ToolCalls">Tool calls to emit, one per model turn, in order.</param>
/// <param name="FinalAnswer">Assistant text produced after the last tool result.</param>
internal sealed record WeatherScenario(
    string Query,
    IReadOnlyList<ScriptedToolCall> ToolCalls,
    string FinalAnswer);
