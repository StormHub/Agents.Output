namespace Agents.Evals.Infrastructure;

/// <summary>
/// A single tool call the scripted chat client should emit.
/// </summary>
/// <param name="Name">Tool name, matching the name the agent registers.</param>
/// <param name="Arguments">Arguments to emit, or <c>null</c> for a no-argument tool.</param>
public sealed record ScriptedToolCall(string Name, IDictionary<string, object?>? Arguments = null);

/// <summary>
/// One case both suites evaluate: the query, the tool calls a correct run makes, the answer the
/// scripted model gives, and the answers a correct run should look like.
/// </summary>
/// <remarks>
/// Each tool may appear at most once per scenario — <see cref="ScriptedChatClient"/> tracks progress
/// by tool name, so a scenario that calls the same tool twice would loop.
/// </remarks>
/// <param name="Name">Short slug used to build the scenario name in a report or result store.</param>
/// <param name="Query">The user query. Matched verbatim by the scripted client.</param>
/// <param name="ToolCalls">Tool calls to emit, one per model turn, in order.</param>
/// <param name="ScriptedAnswer">Assistant text the scripted client produces after the last tool result.</param>
/// <param name="References">
/// Answers a correct run should resemble, for the reference-based evaluators in
/// <c>Agents.Extensions.Evals</c>. The first entry is the primary one: it is the single ground truth
/// handed to <c>F1</c>, <c>Equivalence</c> and <c>Completeness</c>, which take exactly one, and it
/// is also the only one <c>GLEU</c> sees. Keep it equal to <paramref name="ScriptedAnswer"/> unless
/// you mean to measure the wording itself.
/// </param>
public sealed record WeatherScenario(
    string Name,
    string Query,
    IReadOnlyList<ScriptedToolCall> ToolCalls,
    string ScriptedAnswer,
    IReadOnlyList<string> References);
