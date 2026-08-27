namespace Agents.Evals.Infrastructure;

/// <summary>
/// The cases this suite evaluates.
/// </summary>
/// <remarks>
/// Every number in a grounded answer comes from <see cref="StubWeatherTools"/>, so the reference
/// answers are genuinely correct rather than merely plausible, and a grounding check can tell a
/// reported reading from an invented one.
/// </remarks>
public static class WeatherScenarios
{
    /// <summary>A single-tool query: current conditions for one city.</summary>
    public static readonly WeatherScenario Tokyo = new(
        Name: "tokyo",
        Query: "What's the weather in Tokyo?",
        ToolCalls:
        [
            new ScriptedToolCall(
                AgentContract.WeatherToolName,
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
            new ScriptedToolCall(AgentContract.CalendarToolName),
            new ScriptedToolCall(
                AgentContract.WeatherToolName,
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
