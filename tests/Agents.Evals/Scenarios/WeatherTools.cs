using Agents.Api.Tools;

namespace Agents.Evals.Scenarios;

/// <summary>
/// The parts of the production agent's configuration that an evaluation has to reproduce.
/// </summary>
/// <remarks>
/// <para>
/// Both suites need the instructions the agent runs with and the names of the tools it is given,
/// and both would otherwise restate them — which is the one kind of duplication an evaluation
/// cannot afford, because a copy that drifts makes the suite measure something the API never does.
/// </para>
/// <para>
/// This is also the only place that reaches into <c>Agents.Api</c>'s internals, so the suites do
/// not have to. Every name below is derived from the production symbol rather than typed out, so a
/// rename in <c>Agents.Api</c> breaks the build here instead of silently making a check assert
/// nothing.
/// </para>
/// </remarks>
internal static class WeatherTools
{
    /// <summary>Name of the forecast tool, as the model sees it.</summary>
    public const string WeatherToolName = nameof(WeatherForecast.GetWeatherForecast);

    /// <summary>Name of the calendar tool, as the model sees it.</summary>
    public const string CalendarToolName = nameof(CalendarDay.GetToday);
}
