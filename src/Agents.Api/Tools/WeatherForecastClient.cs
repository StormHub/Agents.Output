using System.Text.Json.Serialization;

namespace Agents.Api.Tools;

public sealed class WeatherForecastResponse
{
    [JsonPropertyName("latitude")]
    public required double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public required double Longitude { get; init; }

    [JsonPropertyName("generationtime_ms")]
    public required double GenerationtimeMs { get; init; }

    [JsonPropertyName("utc_offset_seconds")]
    public required int UtcOffsetSeconds { get; init; }

    [JsonPropertyName("timezone")]
    public required string Timezone { get; init; }

    [JsonPropertyName("timezone_abbreviation")]
    public required string TimezoneAbbreviation { get; init; }

    [JsonPropertyName("elevation")]
    public required double Elevation { get; init; }

    [JsonPropertyName("current_units")]
    public required Dictionary<string, string> CurrentUnits { get; init; }

    [JsonPropertyName("current")]
    public required CurrentWeather Current { get; init; }

    [JsonPropertyName("hourly_units")]
    public Dictionary<string, string>? HourlyUnits { get; init; }

    [JsonPropertyName("hourly")]
    public HourlyWeather? Hourly { get; init; }

    [JsonPropertyName("daily_units")]
    public required Dictionary<string, string> DailyUnits { get; init; }

    [JsonPropertyName("daily")]
    public required DailyWeather Daily { get; init; }
}

public sealed class CurrentWeather
{
    [JsonPropertyName("time")]
    public required string Time { get; init; }

    [JsonPropertyName("interval")]
    public required int Interval { get; init; }

    [JsonPropertyName("temperature_2m")]
    public required double Temperature2m { get; init; }

    [JsonPropertyName("relative_humidity_2m")]
    public required int RelativeHumidity2m { get; init; }

    [JsonPropertyName("apparent_temperature")]
    public required double ApparentTemperature { get; init; }

    [JsonPropertyName("weather_code")]
    public required int WeatherCode { get; init; }

    [JsonPropertyName("cloud_cover")]
    public required int CloudCover { get; init; }

    [JsonPropertyName("pressure_msl")]
    public double? PressureMsl { get; init; }

    [JsonPropertyName("wind_speed_10m")]
    public required double WindSpeed10m { get; init; }

    [JsonPropertyName("wind_direction_10m")]
    public required int WindDirection10m { get; init; }

    [JsonPropertyName("wind_gusts_10m")]
    public required double WindGusts10m { get; init; }
}

public sealed class HourlyWeather
{
    [JsonPropertyName("time")]
    public List<string>? Time { get; init; }

    [JsonPropertyName("temperature_2m")]
    public List<double?>? Temperature2m { get; init; }

    [JsonPropertyName("relative_humidity_2m")]
    public List<int?>? RelativeHumidity2m { get; init; }

    [JsonPropertyName("apparent_temperature")]
    public List<double?>? ApparentTemperature { get; init; }

    [JsonPropertyName("precipitation_probability")]
    public List<int?>? PrecipitationProbability { get; init; }

    [JsonPropertyName("precipitation")]
    public List<double?>? Precipitation { get; init; }

    [JsonPropertyName("rain")]
    public List<double?>? Rain { get; init; }

    [JsonPropertyName("showers")]
    public List<double?>? Showers { get; init; }

    [JsonPropertyName("snowfall")]
    public List<double?>? Snowfall { get; init; }

    [JsonPropertyName("weather_code")]
    public List<int?>? WeatherCode { get; init; }

    [JsonPropertyName("cloud_cover")]
    public List<int?>? CloudCover { get; init; }

    [JsonPropertyName("wind_speed_10m")]
    public List<double?>? WindSpeed10m { get; init; }

    [JsonPropertyName("wind_direction_10m")]
    public List<int?>? WindDirection10m { get; init; }

    [JsonPropertyName("wind_gusts_10m")]
    public List<double?>? WindGusts10m { get; init; }
}

public sealed class DailyWeather
{
    [JsonPropertyName("time")]
    public required List<string> Time { get; init; }

    [JsonPropertyName("weather_code")]
    public required List<int> WeatherCode { get; init; }

    [JsonPropertyName("temperature_2m_max")]
    public required List<double> Temperature2mMax { get; init; }

    [JsonPropertyName("temperature_2m_min")]
    public required List<double> Temperature2mMin { get; init; }

    [JsonPropertyName("apparent_temperature_max")]
    public List<double?>? ApparentTemperatureMax { get; init; }

    [JsonPropertyName("apparent_temperature_min")]
    public List<double?>? ApparentTemperatureMin { get; init; }

    [JsonPropertyName("sunrise")]
    public List<string?>? Sunrise { get; init; }

    [JsonPropertyName("sunset")]
    public List<string?>? Sunset { get; init; }

    [JsonPropertyName("precipitation_sum")]
    public required List<double> PrecipitationSum { get; init; }

    [JsonPropertyName("rain_sum")]
    public required List<double?> RainSum { get; init; }

    [JsonPropertyName("showers_sum")]
    public List<double?>? ShowersSum { get; init; }

    [JsonPropertyName("snowfall_sum")]
    public List<double?>? SnowfallSum { get; init; }

    [JsonPropertyName("precipitation_hours")]
    public List<double?>? PrecipitationHours { get; init; }

    [JsonPropertyName("wind_speed_10m_max")]
    public List<double?>? WindSpeed10mMax { get; init; }

    [JsonPropertyName("wind_gusts_10m_max")]
    public List<double?>? WindGusts10mMax { get; init; }

    [JsonPropertyName("wind_direction_10m_dominant")]
    public List<int?>? WindDirection10mDominant { get; init; }
}

internal sealed class WeatherForecastClient(HttpClient httpClient, ILogger<WeatherForecastClient> logger)
{
    private const string ForecastEndpoint = "https://api.open-meteo.com/v1/forecast";
    
    // Variables requested for the "current conditions" block
    internal static readonly string[] CurrentVariables =
    [
        "temperature_2m",
        "relative_humidity_2m",
        "apparent_temperature",
        "is_day",
        "precipitation",
        "weather_code",
        "cloud_cover",
        "pressure_msl",
        "wind_speed_10m",
        "wind_direction_10m",
        "wind_gusts_10m",
    ];

    // Variables requested for the daily forecast block
    internal static readonly string[] DailyVariables =
    [
        "weather_code",
        "temperature_2m_max",
        "temperature_2m_min",
        "apparent_temperature_max",
        "apparent_temperature_min",
        "sunrise",
        "sunset",
        "precipitation_sum",
        "rain_sum",
        "snowfall_sum",
        "wind_speed_10m_max",
        "wind_gusts_10m_max",
        "wind_direction_10m_dominant",
    ];



    /// <summary>
    /// Fetches a weather forecast from the Open-Meteo API.
    /// </summary>
    /// <param name="latitude">WGS84 latitude of the location.</param>
    /// <param name="longitude">WGS84 longitude of the location.</param>
    /// <param name="current">Current-weather variables to include (e.g. temperature_2m, wind_speed_10m).</param>
    /// <param name="hourly">Hourly variables to include (e.g. temperature_2m, precipitation_probability).</param>
    /// <param name="daily">Daily variables to include (e.g. temperature_2m_max, precipitation_sum).</param>
    /// <param name="temperatureUnit">celsius (default) or fahrenheit.</param>
    /// <param name="windSpeedUnit">kmh (default), mph, kn, or ms.</param>
    /// <param name="precipitationUnit">mm (default) or inch.</param>
    /// <param name="timezone">IANA timezone string or "auto" to detect from coordinates.</param>
    /// <param name="forecastDays">Number of forecast days (1–16, default 7).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized <see cref="WeatherForecastResponse"/>, or <c>null</c> on failure.</returns>
    public async Task<WeatherForecastResponse?> GetForecast(
        double latitude,
        double longitude,
        IEnumerable<string>? current = null,
        IEnumerable<string>? hourly = null,
        IEnumerable<string>? daily = null,
        string temperatureUnit = "celsius",
        string windSpeedUnit = "kmh",
        string precipitationUnit = "mm",
        string timezone = "auto",
        int forecastDays = 7,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"latitude={latitude}",
            $"longitude={longitude}",
            $"temperature_unit={temperatureUnit}",
            $"wind_speed_unit={windSpeedUnit}",
            $"precipitation_unit={precipitationUnit}",
            $"timezone={Uri.EscapeDataString(timezone)}",
            $"forecast_days={forecastDays}",
        };

        if (current is not null)
            query.Add($"current={string.Join(",", current)}");

        if (hourly is not null)
            query.Add($"hourly={string.Join(",", hourly)}");

        if (daily is not null)
            query.Add($"daily={string.Join(",", daily)}");

        var url = $"{ForecastEndpoint}?{string.Join("&", query)}";

        logger.LogInformation("Requesting Open-Meteo forecast from {Url}", url);

        try
        {
            var response = await httpClient.GetFromJsonAsync<WeatherForecastResponse>(url, cancellationToken);
            return response;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}

