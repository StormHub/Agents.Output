using Agents.Api.Tools;
using Microsoft.Extensions.AI;

namespace Agents.Extensions.Evals.Infrastructure;

/// <summary>
/// Stand-ins for the production tools that return canned data instead of calling Open-Meteo.
/// </summary>
/// <remarks>
/// <para>
/// The names, parameter names, descriptions and return types mirror the tools registered in
/// <c>Agents.Api.Tools.DependencyInjection.AddTools</c>, so the pipeline under evaluation sees
/// the tool contract production sees, without any network traffic.
/// </para>
/// <para>
/// Canned readings are what make the judged tiers gradeable: because the tool output is fixed and
/// known, there is a real ground truth to hand <c>GroundednessEvaluator</c> and
/// <c>EquivalenceEvaluator</c>. Against live Open-Meteo there would be nothing to compare against
/// that was not itself fetched from the same source.
/// </para>
/// </remarks>
internal static class StubWeatherTools
{
    /// <summary>Tool name the production registration produces for the calendar tool.</summary>
    public const string CalendarToolName = "GetToday";

    /// <summary>Tool name the production registration produces for the weather tool.</summary>
    public const string WeatherToolName = "GetWeatherForecast";

    /// <summary>Fixed "today" so forecast labels and date assertions stay stable.</summary>
    public static readonly DateOnly FixedToday = new(2026, 3, 14);

    public static AIFunction Calendar() =>
        AIFunctionFactory.Create(
            () => new Today(
                Utc: FixedToday.ToString("yyyy-MM-dd"),
                UtcOffset: "00:00",
                Timezone: "UTC"),
            CalendarToolName,
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
            WeatherToolName,
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
