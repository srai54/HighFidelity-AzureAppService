using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebApp.Configuration;

namespace WebApp.Controllers;

/// <summary>
/// Demonstrates injecting Key-Vault-backed configuration via the options
/// pattern. Never echoes a real secret over HTTP — everything returned here
/// is redacted, purely to prove the binding worked. See docs/KEYVAULT.md.
/// </summary>
[ApiController]
[Route("api/payment-gateway")]
public class PaymentGatewayController : ControllerBase
{
    private readonly IOptionsSnapshot<PaymentGatewayOptions> _snapshotOptions;
    private readonly IOptionsMonitor<PaymentGatewayOptions> _monitorOptions;

    public PaymentGatewayController(
        IOptionsSnapshot<PaymentGatewayOptions> snapshotOptions,
        IOptionsMonitor<PaymentGatewayOptions> monitorOptions)
    {
        _snapshotOptions = snapshotOptions;
        _monitorOptions = monitorOptions;
    }

    /// <summary>
    /// IOptionsSnapshot — recomputed once per request scope. Fine for most
    /// config; the request-scoped recompute is what lets it pick up a config
    /// reload without redeploying, at a small per-request cost.
    /// </summary>
    [HttpGet("config-snapshot")]
    public ActionResult GetViaSnapshot() => Ok(Redact(_snapshotOptions.Value));

    /// <summary>
    /// IOptionsMonitor — a singleton that raises OnChange when the underlying
    /// source reloads (appsettings.json edited, or Key Vault's own reload
    /// interval elapses). Prefer this over IOptionsSnapshot in a singleton
    /// service, which can't take a scoped IOptionsSnapshot as a dependency.
    /// </summary>
    [HttpGet("config-monitor")]
    public ActionResult GetViaMonitor() => Ok(Redact(_monitorOptions.CurrentValue));

    private static object Redact(PaymentGatewayOptions options) => new
    {
        options.BaseUrl,
        ApiKey = MaskSecret(options.ApiKey),
        ApiSecret = MaskSecret(options.ApiSecret)
    };

    private static string MaskSecret(string value) =>
        string.IsNullOrEmpty(value) ? "(not configured)" : $"{value[..Math.Min(2, value.Length)]}***";
}
