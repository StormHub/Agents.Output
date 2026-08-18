using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Agents.Api.Tools;

internal static class DependencyInjection
{
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        
        // Calendar 
        services.AddTransient<CalendarDay>();
        services.AddTransient<AITool>(provider =>
        {
            var calendarDay = provider.GetRequiredService<CalendarDay>();
            return AIFunctionFactory.Create(calendarDay.GetToday, 
                serializerOptions: JsonSerializerOptions.Web);
        });
        
        // Weather
        services.AddHttpClient(nameof(WeatherForecastClient))
            .AddStandardResilienceHandler();
        services.AddTransient<WeatherForecastClient>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(WeatherForecastClient));

            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

            return new WeatherForecastClient(httpClient, loggerFactory.CreateLogger<WeatherForecastClient>());
        });

        services.AddTransient<WeatherForecast>();
        services.AddTransient<AITool>(provider =>
        {
            var weatherForecast = provider.GetRequiredService<WeatherForecast>();
            return AIFunctionFactory.Create(weatherForecast.GetWeatherForecast, 
                serializerOptions: JsonSerializerOptions.Web);
        });

        return services;
    }
}