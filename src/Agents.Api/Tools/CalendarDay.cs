using System.ComponentModel;

namespace Agents.Api.Tools;

public record Today(string Utc, string UtcOffset, string Timezone);

internal sealed class CalendarDay(TimeProvider timeProvider, ILogger<CalendarDay> logger)
{
    [Description("Get today's date in UTC in yyyy-MM-dd format.")]
    public Today GetToday()
    {
        var utcNow = timeProvider.GetUtcNow();
        var timezone = timeProvider.LocalTimeZone;
        
        var today = DateOnly.FromDateTime(utcNow.UtcDateTime);
        logger.LogInformation("{Tool} Today's date is {Date}", nameof(GetToday), today);
        
        return new Today(
            Utc: today.ToString("yyyy-MM-dd"),
            UtcOffset: timezone.BaseUtcOffset.ToString(@"hh\:mm"),
            Timezone: timezone.DisplayName);
    }
    
    internal static string GetShortWeekday(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Sunday => "Sun",
        DayOfWeek.Monday => "Mon",
        DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed",
        DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri",
        DayOfWeek.Saturday => "Sat",
        _ => throw new InvalidOperationException($"Unknown value {dayOfWeek}")
    };    
}