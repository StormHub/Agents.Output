namespace Agents.Evals.Scenarios;

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


/// <summary>
/// The cases this suite evaluates.
/// </summary>
/// <remarks>
/// Every number in a grounded answer comes from <see cref="StubWeatherTools"/>, so the reference
/// answers are genuinely correct rather than merely plausible, and a grounding check can tell a
/// reported reading from an invented one.
/// </remarks>
internal static class WeatherScenarios
{
    /// <summary>A single-tool query: current conditions for one city.</summary>
    public static readonly WeatherScenario Tokyo = new(
        Name: "tokyo",
        Query: "What's the weather in Tokyo?",
        ToolCalls:
        [
            new ScriptedToolCall(
                WeatherTools.WeatherToolName,
                new Dictionary<string, object?>
                {
                    ["latitude"] = 35.6762,
                    ["longitude"] = 139.6503,
                    ["location"] = "Tokyo",
                }),
        ],
        ScriptedAnswer:
            "It is currently 22.4°C and cloudy in Tokyo, with 63% humidity and a north-easterly "
            + "breeze at 11.2 km/h.",
        References:
        [
            "It is currently 22.4°C and cloudy in Tokyo, with 63% humidity and a north-easterly "
            + "breeze at 11.2 km/h.",
            "Tokyo is cloudy at the moment: 22.4°C, 63% humidity, and a north-easterly wind of "
            + "11.2 km/h.",
        ]);

    /// <summary>
    /// A date-relative query, which needs two tools: the calendar to resolve "tomorrow", then the
    /// forecast.
    /// </summary>
    public static readonly WeatherScenario Paris = new(
        Name: "paris",
        Query: "Will it rain in Paris tomorrow?",
        ToolCalls:
        [
            new ScriptedToolCall(WeatherTools.CalendarToolName),
            new ScriptedToolCall(
                WeatherTools.WeatherToolName,
                new Dictionary<string, object?>
                {
                    ["latitude"] = 48.8566,
                    ["longitude"] = 2.3522,
                    ["location"] = "Paris",
                }),
        ],
        ScriptedAnswer:
            "Tomorrow in Paris looks cloudy, with a high of 21°C and a low of 12°C, and no rain "
            + "is expected.",
        References:
        [
            "Tomorrow in Paris looks cloudy, with a high of 21°C and a low of 12°C, and no rain "
            + "is expected.",
            "Paris should be cloudy tomorrow — around 21°C at the warmest and 12°C at the "
            + "coolest, with no rain.",
        ]);

    /// <summary>
    /// The control: a model answering from its weights. Confident numbers, no tool call, nothing
    /// behind them.
    /// </summary>
    /// <remarks>
    /// A grounding check that never fails is asserting nothing, so this scenario exists to make it
    /// fail. Its reference is the answer the tools would have supported, which is also what pushes
    /// its BLEU, GLEU and F1 well below the grounded scenarios.
    /// </remarks>
    public static readonly WeatherScenario UngroundedBerlin = new(
        Name: "berlin-control",
        Query: "What's the weather in Berlin?",
        ToolCalls: [],
        ScriptedAnswer: "It's 25°C and sunny in Berlin right now, a lovely day to be outside.",
        References:
        [
            "It is currently 22.4°C and cloudy in Berlin, with 63% humidity and a north-easterly "
            + "breeze at 11.2 km/h.",
        ]);

    /// <summary>Scenarios whose answers are supported by tool output.</summary>
    public static IReadOnlyList<WeatherScenario> Grounded => [Tokyo, Paris];

    /// <summary>Every scenario, including the ungrounded control.</summary>
    public static IReadOnlyList<WeatherScenario> All => [Tokyo, Paris, UngroundedBerlin];
}
