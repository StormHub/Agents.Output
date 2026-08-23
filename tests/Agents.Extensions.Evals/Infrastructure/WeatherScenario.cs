namespace Agents.Extensions.Evals.Infrastructure;

/// <summary>
/// A single tool call the scripted chat client should emit.
/// </summary>
/// <param name="Name">Tool name, matching the name the pipeline registers.</param>
/// <param name="Arguments">Arguments to emit, or <c>null</c> for a no-argument tool.</param>
internal sealed record ScriptedToolCall(string Name, IDictionary<string, object?>? Arguments = null);

/// <summary>
/// One evaluated case: the query, what a scripted model does with it, and what a good answer
/// looks like.
/// </summary>
/// <remarks>
/// Each tool may appear at most once per scenario — <see cref="ScriptedChatClient"/> tracks
/// progress by tool name, so a scenario that calls the same tool twice would loop.
/// </remarks>
/// <param name="Name">Scenario name, used to build the name recorded in the result store.</param>
/// <param name="Query">The user query. Matched verbatim by the scripted client.</param>
/// <param name="ToolCalls">Tool calls the scripted client emits, one per model turn, in order.</param>
/// <param name="ScriptedAnswer">Assistant text the scripted client produces after the last tool result.</param>
/// <param name="References">
/// Reference answers for the NLP evaluators and, via the first entry, the ground truth handed to
/// <c>EquivalenceEvaluator</c>. BLEU and GLEU score against the best-matching reference.
/// </param>
internal sealed record WeatherScenario(
    string Name,
    string Query,
    IReadOnlyList<ScriptedToolCall> ToolCalls,
    string ScriptedAnswer,
    IReadOnlyList<string> References);
