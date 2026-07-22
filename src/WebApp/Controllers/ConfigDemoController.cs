using Microsoft.AspNetCore.Mvc;

namespace WebApp.Controllers;

// Demonstrates reading configuration DIRECTLY via IConfiguration injected into
// a controller — the "stringly-typed" alternative to the IOptions pattern that
// PaymentGatewayController uses. Both read from the SAME merged configuration;
// they differ only in how your code receives the values. See
// docs/ICONFIGURATION.md for when to prefer which.
[ApiController]
[Route("api/config-demo")]
public class ConfigDemoController : ControllerBase
{
    private readonly IConfiguration _configuration;

    // IConfiguration is registered by the host automatically — you never wire
    // it up in Program.cs; just ask for it in a constructor and DI hands you
    // the single merged configuration (all providers layered together).
    public ConfigDemoController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Read a single value by its colon-separated key path. Note this is
    // "stringly typed": a typo in the key ("PaymentGatewya:BaseUrl") compiles
    // fine and just returns null at runtime — the main downside vs. IOptions.
    [HttpGet("value")]
    public IActionResult GetSingleValue()
    {
        var baseUrl = _configuration["PaymentGateway:BaseUrl"];

        // GetValue<T> converts to a type and lets you supply a default when the
        // key is absent — handy for optional numeric/bool settings.
        var reloadSeconds = _configuration.GetValue<int>("PaymentGateway:ReloadSeconds", 30);

        return Ok(new { baseUrl, reloadSeconds });
    }

    // Read a whole section as a strongly-ish-typed object with .Get<T>() — a
    // one-off bind without registering it as IOptions in Program.cs. Secrets
    // are masked so this never echoes a real ApiKey/ApiSecret over HTTP.
    [HttpGet("section")]
    public IActionResult GetSection()
    {
        var section = _configuration.GetSection("PaymentGateway").Get<PaymentGatewaySnapshot>();
        if (section is null)
        {
            return NotFound(new { error = "PaymentGateway section not found." });
        }

        return Ok(new
        {
            section.BaseUrl,
            ApiKey = Mask(section.ApiKey),
            ApiSecret = Mask(section.ApiSecret)
        });
    }

    // Shows which configuration PROVIDER a given key's value ultimately came
    // from — the fastest way to understand the "last provider wins" layering
    // when a value isn't what you expected (e.g. an env var silently
    // overriding appsettings.json). Only exposed for learning; you wouldn't
    // ship a "dump my config providers" endpoint in a real API.
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        // IConfigurationRoot exposes the ordered provider list; earlier entries
        // are overridden by later ones for the same key.
        var providers = (_configuration as IConfigurationRoot)?.Providers
            .Select(p => p.GetType().Name)
            .ToList();

        return Ok(new { providerOrderEarliestToLatest = providers });
    }

    private static string Mask(string? value) =>
        string.IsNullOrEmpty(value) ? "(not configured)" : $"{value[..Math.Min(2, value.Length)]}***";

    private sealed class PaymentGatewaySnapshot
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }
}
