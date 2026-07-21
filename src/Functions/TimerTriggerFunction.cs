using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Functions;

/// <summary>
/// Runs on a schedule (a cron expression, NCRONTAB format), not in response
/// to any request — "clean up old rows every night at 2am" is this shape.
/// See docs/FUNCTION_APPS.md.
/// </summary>
public class TimerTriggerFunction
{
    private readonly ILogger<TimerTriggerFunction> _logger;

    public TimerTriggerFunction(ILogger<TimerTriggerFunction> logger) => _logger = logger;

    // NCRONTAB has 6 fields (includes seconds): {second} {minute} {hour} {day} {month} {day-of-week}.
    // "0 */5 * * * *" = at second 0, every 5th minute, every hour/day/month/weekday = every 5 minutes.
    [Function("TimerTriggerFunction")]
    public void Run([TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation(
            "TimerTriggerFunction fired at {Time}. Next scheduled run: {Next}",
            DateTime.UtcNow,
            timerInfo.ScheduleStatus?.Next);
    }
}
