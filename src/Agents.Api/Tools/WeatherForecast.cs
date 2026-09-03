using System.ComponentModel;

namespace Agents.Api.Tools;

public sealed class WeatherResult
{
    public required string Location { get; init; }
    
    public required double Latitude { get; init; }
        
    public required  double Longitude { get; init; }
    
    public required double Temperature { get; init; }
    
    public required double FeelsLike { get; init; }
    
    public required string Condition { get; init; }
    
    public required int Humidity { get; init; }
    
    public required double WindSpeed { get; init; }
    
    public required string WindDirection { get; init; } // "N", "NE", "E", etc.
    
    public IReadOnlyCollection<ForecastDay> Forecast { get; set; } = [];
}

public sealed class ForecastDay
{
    public required string Label { get; init; }
    
    public required DateOnly Date { get; init; }
    
    public required double HighTemp { get; init; }
    
    public required double LowTemp { get; init; }
    
    public required string Condition { get; init; }
    
    public required double Precipitation { get; init; }
}

internal sealed class WeatherForecast(WeatherForecastClient weatherClient, TimeProvider timeProvider, ILogger<WeatherForecast> logger)
{
    [Description("Get the current weather conditions and 7-day daily forecast for a location.")]
    public async Task<WeatherResult?> GetWeatherForecast(
        [Description("WGS84 latitude of the location (e.g. 48.8566 for Paris).")] double latitude,
        [Description("WGS84 longitude of the location (e.g. 2.3522 for Paris).")] double longitude,
        [Description("Name of the location (e.g. Tokyo, Japan).")] string location)
    {
        logger.LogInformation("{Tool} Lat={Latitude} Lon={Longitude}", nameof(GetWeatherForecast), latitude, longitude);

        var response = await weatherClient.GetForecast(
            latitude,
            longitude,
            current: WeatherForecastClient.CurrentVariables,
            daily: WeatherForecastClient.DailyVariables);

        return response is not null ? MapToWeatherResult(response, latitude, longitude, location) : null;
    }

    private WeatherResult MapToWeatherResult(WeatherForecastResponse response, double latitude, double longitude, string location)
    {
        var result = new WeatherResult
        {
            Location = location,
            Latitude = latitude,
            Longitude = longitude,
            Temperature = response.Current.Temperature2m,
            FeelsLike = response.Current.ApparentTemperature,
            Condition = GetWeatherCondition(response.Current.WeatherCode),
            Humidity = response.Current.RelativeHumidity2m,
            WindSpeed = response.Current.WindSpeed10m,
            WindDirection = GetWindDirection(response.Current.WindDirection10m),
        };

        // Map daily forecast
        var utcNow = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        var tomorrow = today.AddDays(1);
        
        var forecastDays = new List<ForecastDay>();
        for (var i = 0; i < response.Daily.Time.Count; i++)
        {
            if (DateTime.TryParse(response.Daily.Time[i], out var date))
            {
                var day = DateOnly.FromDateTime(date);
                forecastDays.Add(
                    new()
                    {
                        Label = day switch
                            {
                                _ when day == today => "Today",
                                _ when day == tomorrow => "Tomorrow",
                                _ => CalendarDay.GetShortWeekday(day.DayOfWeek)
                            },
                        Date = DateOnly.FromDateTime(date),
                        HighTemp = response.Daily.Temperature2mMax[i],
                        LowTemp = response.Daily.Temperature2mMin[i],
                        Condition = GetWeatherCondition(response.Daily.WeatherCode[i]),
                        Precipitation = response.Daily.PrecipitationSum[i]
                    });
            }
        }

        result.Forecast = forecastDays;

        return result;
    }
    

    private static string GetWeatherCondition(int weatherCode)
    {
        // WMO Weather interpretation codes
        return weatherCode switch
        {
            0 => "sunny",
            1 or 2 => "cloudy",
            3 => "cloudy",
            45 or 48 => "cloudy", // Foggy
            51 or 53 or 55 => "rainy", // Drizzle
            61 or 63 or 65 => "rainy",
            71 or 73 or 75 => "snowy",
            77 => "snowy",
            80 or 81 or 82 => "rainy", // Rain showers
            85 or 86 => "snowy", // Snow showers
            95 or 96 or 99 => "stormy", // Thunderstorm
            _ => "cloudy",
        };
    }

    private static string GetWindDirection(int degrees)
    {
        // Convert degrees to cardinal direction
        var direction = ((degrees + 11) / 22) % 16;
        return direction switch
        {
            0 => "N",
            1 => "NNE",
            2 => "NE",
            3 => "ENE",
            4 => "E",
            5 => "ESE",
            6 => "SE",
            7 => "SSE",
            8 => "S",
            9 => "SSW",
            10 => "SW",
            11 => "WSW",
            12 => "W",
            13 => "WNW",
            14 => "NW",
            15 => "NNW",
            _ => "N",
        };
    }
}
