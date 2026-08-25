using Agents.Api.Tools;
using Microsoft.Extensions.AI;

namespace Agents.Api.Evals.Infrastructure;

/// <summary>
/// Stand-ins for the production tools that return canned data instead of calling Open-Meteo.
/// </summary>
/// <remarks>
/// The names, parameter names and return types deliberately mirror the real tools registered in
/// <c>Agents.Api.Tools.DependencyInjection.AddTools</c>, so the evaluation suite exercises the
/// same tool contract the agent sees in production without any network traffic. If a production
/// tool is renamed or its arguments change, the checks in <see cref="WeatherAgentChecks"/> start
/// failing here first.
/// </remarks>
internal static class StubWeatherTools
{
    /// <summary>Fixed "today" so forecast labels and date assertions stay stable.</summary>
    public static readonly DateOnly FixedToday = new(2026, 3, 14);

    public static AIFunction Calendar() =>
        AIFunctionFactory.Create(
            () => new Today(
                Utc: FixedToday.ToString("yyyy-MM-dd"),
                UtcOffset: "00:00",
                Timezone: "UTC"),
            WeatherAgentChecks.CalendarToolName,
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
            WeatherAgentChecks.WeatherToolName,
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
