using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Functions;

/// <summary>
/// Runs when something calls its URL — same mental model as a
/// Controller action, just hosted by the Functions runtime instead of
/// Kestrel/IIS. See docs/FUNCTION_APPS.md.
/// </summary>
public class HttpTriggerFunction
{
    private readonly ILogger<HttpTriggerFunction> _logger;

    public HttpTriggerFunction(ILogger<HttpTriggerFunction> logger) => _logger = logger;

    [Function("HttpTriggerFunction")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "greet/{name}")] HttpRequest req,
        string name)
    {
        _logger.LogInformation("HttpTriggerFunction invoked for {Name}", name);
        return new OkObjectResult(new { message = $"Hello, {name}!", triggeredAtUtc = DateTime.UtcNow });
    }
}
