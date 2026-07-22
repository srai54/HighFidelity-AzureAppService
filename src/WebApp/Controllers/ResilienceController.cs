using Microsoft.AspNetCore.Mvc;

namespace WebApp.Controllers;

// Demonstrates Polly-style resilience (retry, circuit breaker, timeout) via
// Microsoft.Extensions.Http.Resilience's AddStandardResilienceHandler, wired
// up in Program.cs on the "ResilientClient" named HttpClient. See
// docs/RESILIENCE_POLLY.md for the theory and interview questions.
//
// FlakyTarget stands in for an unreliable downstream dependency (a flaky
// third-party API, a payment gateway having a bad day). CallFlaky and
// HammerFlaky call it *through* the resilience-wrapped HttpClient so you can
// watch retries recover transient failures, and watch the circuit breaker
// trip when failures are sustained instead of transient.
[ApiController]
[Route("api/resilience")]
public class ResilienceController : ControllerBase
{
    private static int _callCount;
    private static bool _alwaysFail;

    private readonly IHttpClientFactory _httpClientFactory;

    public ResilienceController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    // Fails 2 out of every 3 calls (or every call, if always-fail is toggled on).
    [HttpGet("flaky-target")]
    public IActionResult FlakyTarget()
    {
        var attempt = Interlocked.Increment(ref _callCount);
        if (_alwaysFail || attempt % 3 != 0)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { attempt, ok = false });
        }

        return Ok(new { attempt, ok = true });
    }

    [HttpPost("flaky-target/reset")]
    public IActionResult ResetFlakyTarget()
    {
        Interlocked.Exchange(ref _callCount, 0);
        _alwaysFail = false;
        return Ok(new { reset = true });
    }

    // Forces flaky-target to fail every call — use this to demo the circuit
    // breaker tripping, as opposed to the default mod-3 flakiness which only
    // demos retries recovering a transient failure.
    [HttpPost("flaky-target/always-fail/{enabled:bool}")]
    public IActionResult SetAlwaysFail(bool enabled)
    {
        _alwaysFail = enabled;
        return Ok(new { alwaysFail = enabled });
    }

    // Single call through the resilience pipeline. With default (mod-3)
    // flakiness this almost always ends up 200 OK, because the retry
    // strategy silently absorbs the 503s underneath.
    [HttpGet("call-flaky")]
    public async Task<IActionResult> CallFlaky()
    {
        var client = _httpClientFactory.CreateClient("ResilientClient");
        try
        {
            var response = await client.GetAsync("/api/resilience/flaky-target");
            var body = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, body.Length == 0 ? null : System.Text.Json.JsonDocument.Parse(body).RootElement);
        }
        catch (Exception ex)
        {
            // A BrokenCircuitException here means the circuit is open — the
            // resilience handler refused to even attempt the call.
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { exception = ex.GetType().Name, ex.Message });
        }
    }

    // Fires `times` calls back-to-back through the resilience pipeline and
    // reports what happened to each one — the fastest way to actually see
    // retries and the circuit breaker doing their job instead of trusting
    // that the config is correct.
    [HttpGet("hammer-flaky/{times:int}")]
    public async Task<IActionResult> HammerFlaky(int times)
    {
        var client = _httpClientFactory.CreateClient("ResilientClient");
        var results = new List<string>();

        for (var i = 0; i < times; i++)
        {
            try
            {
                var response = await client.GetAsync("/api/resilience/flaky-target");
                results.Add($"{i}: HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                results.Add($"{i}: {ex.GetType().Name}");
            }
        }

        return Ok(results);
    }
}
