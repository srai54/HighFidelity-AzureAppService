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

    /// <summary>
    /// UNHANDLED exception demo. This throws and does NOT catch — the function
    /// invocation fails, the caller gets a 500, and App Insights records BOTH a
    /// failed request AND an exception telemetry item (with stack trace),
    /// correlated under the same operation id. No telemetry code needed: an
    /// exception that escapes the function is captured automatically. See the
    /// "Exceptions and errors" section in docs/APP_INSIGHTS_FUNCTIONS_DEMO.md.
    /// </summary>
    [Function("Function3Unhandled")]
    public IActionResult Function3Unhandled(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "function-3-unhandled")] HttpRequest req)
    {
        _logger.LogInformation("Function3 invoked — about to throw an unhandled exception");
        throw new InvalidOperationException("function-3 blew up on purpose (unhandled) — this is the demo exception.");
    }

    /// <summary>
    /// HANDLED error demo. This catches its own exception and reports it via
    /// _logger.LogError(ex, ...). The function still returns a controlled 500
    /// (a clean error response), but the exception is recorded in App Insights
    /// as an exception/error trace — this is the pattern for "I expected this
    /// could fail, I want it logged, but I don't want the process to crash."
    /// </summary>
    [Function("Function4Handled")]
    public IActionResult Function4Handled(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "function-4-handled")] HttpRequest req)
    {
        try
        {
            _logger.LogInformation("Function4 invoked — about to throw a handled exception");
            throw new TimeoutException("function-4 simulated a downstream timeout (handled).");
        }
        catch (Exception ex)
        {
            // LogError with the exception object attaches the full exception
            // (type, message, stack) to the log record — it surfaces in App
            // Insights' Failures/exceptions, correlated to this request.
            _logger.LogError(ex, "Function4 caught a downstream failure for request {Path}", req.Path);
            return new ObjectResult(new { error = "handled", detail = ex.Message }) { StatusCode = 500 };
        }
    }
}
