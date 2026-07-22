using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Functions;

/// <summary>
/// Two deliberately trivial HTTP functions used to demonstrate Application
/// Insights for a Function App: call each one, then watch the invocations,
/// timings, and log traces show up in Live Metrics / Performance in the Azure
/// Portal. See docs/APP_INSIGHTS_FUNCTIONS_DEMO.md for the deploy + monitor
/// walkthrough, and docs/APPLICATION_INSIGHTS.md for the telemetry theory.
///
/// The ILogger calls matter here: in a Function App wired to Application
/// Insights, these log lines are automatically shipped as "trace" telemetry
/// and correlated to the request that produced them — no telemetry-specific
/// code needed for that.
/// </summary>
public class AppInsightsDemoFunctions
{
    private readonly ILogger<AppInsightsDemoFunctions> _logger;

    public AppInsightsDemoFunctions(ILogger<AppInsightsDemoFunctions> logger) => _logger = logger;

    [Function("Function1")]
    public IActionResult Function1(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "function-1")] HttpRequest req)
    {
        // Shows up as a trace in App Insights, tied to this request's operation id.
        _logger.LogInformation("Function1 invoked at {Utc}", DateTime.UtcNow);
        return new OkObjectResult("function-1");
    }

    [Function("Function2")]
    public IActionResult Function2(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "function-2")] HttpRequest req)
    {
        _logger.LogInformation("Function2 invoked at {Utc}", DateTime.UtcNow);
        return new OkObjectResult("function-2");
    }
}
