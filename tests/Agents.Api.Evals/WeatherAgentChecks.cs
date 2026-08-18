using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Agents.Api.Evals;

/// <summary>
/// The checks the weather agent is evaluated against.
/// </summary>
/// <remarks>
/// The agent is a tool router, so most of the signal is in whether it called the right tool with
/// sane arguments — not in the prose. These checks are all local: they read the conversation that
/// <c>EvaluateAsync</c> captured and need no judge model.
/// </remarks>
internal static class WeatherAgentChecks
{
    internal const string WeatherToolName = "GetWeatherForecast";

    internal const string CalendarToolName = "GetToday";

    /// <summary>Matches a temperature claim such as "22°C", "-4 degrees" or "18 C".</summary>
    private static readonly Regex TemperatureClaim = new(
        @"-?\d+(\.\d+)?\s*(°|degrees\b|\bC\b|\bF\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Renames a check so several checks of the same built-in kind can coexist in one
    /// <see cref="LocalEvaluator"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="LocalEvaluator"/> stores each result in a dictionary keyed by
    /// <c>EvalCheckResult.CheckName</c>, and the built-in checks hard-code that name — every
    /// <c>ToolCalledCheck</c> variant reports "tool_called_check". Two unrenamed checks of the
    /// same kind therefore silently overwrite each other and only the last one is reported.
    /// <c>FunctionEvaluator.Create</c> does not help: it only fills in a <c>null</c> name, and the
    /// built-ins always supply one.
    /// </remarks>
    public static EvalCheck Named(string name, EvalCheck check) =>
        item => check(item) with { CheckName = name };

    /// <summary>The agent answered a weather question by calling the weather tool.</summary>
    public static EvalCheck CalledWeatherTool() =>
        Named("called_weather_tool", EvalChecks.ToolCalledCheck(WeatherToolName));

    /// <summary>
    /// A date-relative question ("tomorrow", "this weekend") was grounded on the calendar tool
    /// before the forecast was fetched, rather than on the model's idea of the current date.
    /// </summary>
    public static EvalCheck GroundedOnCalendar() =>
        Named(
            "grounded_on_calendar",
            EvalChecks.ToolCalledCheck(ToolCalledMode.All, CalendarToolName, WeatherToolName));

    /// <summary>The agent produced a substantive answer rather than an empty turn.</summary>
    public static EvalCheck Answered() =>
        Named("answered", EvalChecks.NonEmpty(minLength: 20));

    /// <summary>
    /// The agent did not state a temperature it never looked up.
    /// </summary>
    /// <remarks>
    /// This is the failure that matters most for this agent: a small local model answering
    /// "it's 25°C and sunny in Berlin" straight from its weights, with no tool call behind it.
    /// </remarks>
    public static EvalCheck NoUngroundedWeatherClaim() => item =>
    {
        var calledWeatherTool = ToolCalls(item)
            .Any(call => string.Equals(call.Name, WeatherToolName, StringComparison.OrdinalIgnoreCase));

        var claimsTemperature = TemperatureClaim.IsMatch(item.Response);
        var passed = !claimsTemperature || calledWeatherTool;

        var reason = passed
            ? claimsTemperature
                ? "Temperature claim is backed by a weather tool call."
                : "Response makes no temperature claim."
            : $"Response states a temperature but never called {WeatherToolName}.";

        return new EvalCheckResult(passed, reason, "no_ungrounded_weather_claim");
    };

    /// <summary>
    /// Coordinates passed to the weather tool are inside the valid WGS84 range.
    /// </summary>
    /// <remarks>
    /// Catches a model that transposes latitude and longitude, or invents coordinates outright.
    /// Passes when the tool was not called — <see cref="CalledWeatherTool"/> owns that failure.
    /// </remarks>
    public static EvalCheck PlausibleCoordinates() => item =>
    {
        var calls = ToolCalls(item)
            .Where(call => string.Equals(call.Name, WeatherToolName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (calls.Count == 0)
        {
            return new EvalCheckResult(true, $"{WeatherToolName} was not called.", "plausible_coordinates");
        }

        var offenders = new List<string>();
        foreach (var call in calls)
        {
            var latitude = ArgumentAsDouble(call, "latitude");
            var longitude = ArgumentAsDouble(call, "longitude");

            if (latitude is null || longitude is null)
            {
                offenders.Add($"{call.CallId}: missing latitude/longitude");
                continue;
            }

            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            {
                offenders.Add(FormattableString.Invariant($"{call.CallId}: ({latitude}, {longitude})"));
            }
        }

        var passed = offenders.Count == 0;
        var reason = passed
            ? $"All {calls.Count} coordinate pair(s) are in range."
            : $"Out-of-range coordinates: {string.Join("; ", offenders)}";

        return new EvalCheckResult(passed, reason, "plausible_coordinates");
    };

    /// <summary>
    /// The answer names the location that was actually looked up.
    /// </summary>
    /// <remarks>
    /// Derives the expected location from the tool call itself rather than from per-query
    /// configuration, so it holds for any query. Compares on the first comma-separated segment so
    /// a lookup for "Tokyo, Japan" is satisfied by an answer that says "Tokyo".
    /// </remarks>
    public static EvalCheck AnswerNamesLocation() => item =>
    {
        var locations = ToolCalls(item)
            .Where(call => string.Equals(call.Name, WeatherToolName, StringComparison.OrdinalIgnoreCase))
            .Select(call => ArgumentAsString(call, "location"))
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Select(location => location!.Split(',')[0].Trim())
            .Where(location => location.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (locations.Count == 0)
        {
            return new EvalCheckResult(true, "No location was looked up.", "answer_names_location");
        }

        var missing = locations
            .Where(location => !item.Response.Contains(location, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var passed = missing.Count == 0;
        var reason = passed
            ? $"Answer names: {string.Join(", ", locations)}"
            : $"Answer omits looked-up location(s): {string.Join(", ", missing)}";

        return new EvalCheckResult(passed, reason, "answer_names_location");
    };

    /// <summary>The checks that apply to every weather query.</summary>
    public static LocalEvaluator BaselineEvaluator() => new(
        CalledWeatherTool(),
        Answered(),
        NoUngroundedWeatherClaim(),
        PlausibleCoordinates(),
        AnswerNamesLocation());

    private static IEnumerable<FunctionCallContent> ToolCalls(EvalItem item) =>
        item.Conversation
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>();

    private static string? ArgumentAsString(FunctionCallContent call, string name) =>
        Argument(call, name) switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            var value => value.ToString(),
        };

    private static double? ArgumentAsDouble(FunctionCallContent call, string name) =>
        Argument(call, name) switch
        {
            null => (double?)null,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

    private static object? Argument(FunctionCallContent call, string name)
    {
        if (call.Arguments is null)
        {
            return null;
        }

        foreach (var pair in call.Arguments)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
