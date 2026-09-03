using Agents.Api.Tools;
using Agents.Evals.Infrastructure;
using Microsoft.Extensions.AI;

namespace Agents.Evals.Scenarios;

/// <summary>
/// Stand-ins for the production tools that return canned data instead of calling Open-Meteo.
/// </summary>
/// <remarks>
/// <para>
/// The names come from <see cref="WeatherTools"/>, and the parameter names, descriptions and
/// return types deliberately mirror the real tools registered in
/// <c>Agents.Api.Tools.DependencyInjection.AddTools</c>, so both suites exercise the same tool
/// contract the model sees in production without any network traffic.
/// </para>
/// <para>
/// Canning the readings is not a convenience, it is what makes the judged tiers gradeable: because
/// the numbers are fixed and known, <c>Groundedness</c> and <c>Equivalence</c> have a real ground
/// truth rather than a plausible one. Against live Open-Meteo there would be nothing to compare an
/// answer against that was not itself fetched from the same source.
/// </para>
/// </remarks>
public static class StubWeatherTools
{
    /// <summary>Fixed "today" so forecast labels and date assertions stay stable across runs.</summary>
    public static readonly DateOnly FixedToday = new(2026, 3, 14);

    /// <summary>Both tools, in the order the production agent registers them.</summary>
    public static IReadOnlyList<AITool> All() => [Calendar(), Weather()];

    public static AIFunction Calendar() =>
        AIFunctionFactory.Create(
            () => new Today(
                Utc: FixedToday.ToString("yyyy-MM-dd"),
                UtcOffset: "00:00",
                Timezone: "UTC"),
            WeatherTools.CalendarToolName,
            "Get today's date in UTC in yyyy-MM-dd format.");

    public static AIFunction Weather() =>
        AIFunctionFactory.Create(
            (double latitude, double longitude, string location) => new WeatherResult
            {
                Location = location,
                Latitude = latitude,
                Longitude = longitude,
                Temperature = 22.4,
                FeelsLike = 21.8,
                Condition = "cloudy",
                Humidity = 63,
                WindSpeed = 11.2,
                WindDirection = "NE",
                Forecast = BuildForecast(),
            },
            WeatherTools.WeatherToolName,
            "Get the current weather conditions and 7-day daily forecast for a location.");

    private static List<ForecastDay> BuildForecast()
    {
        var days = new List<ForecastDay>();

        for (var offset = 0; offset < 7; offset++)
        {
            var date = FixedToday.AddDays(offset);
            var label = offset switch
            {
                0 => "Today",
                1 => "Tomorrow",
                _ => CalendarDay.GetShortWeekday(date.DayOfWeek),
            };

            days.Add(new ForecastDay
            {
                Label = label,
                Date = date,
                HighTemp = 20 + offset,
                LowTemp = 11 + offset,
                Condition = offset % 3 == 0 ? "rainy" : "cloudy",
                Precipitation = offset % 3 == 0 ? 4.2 : 0.0,
            });
        }

        return days;
    }
}
