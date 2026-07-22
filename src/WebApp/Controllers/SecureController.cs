using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApp.Controllers;

// [Authorize] here means "this endpoint requires a valid Entra ID-issued JWT
// bearer token" once AddMicrosoftIdentityWebApi is wired up in Program.cs
// (only happens when AzureAd:TenantId is configured — see
// docs/MANAGED_IDENTITY_ENTRA_ID.md). Whoami echoes back the token's claims,
// which is the fastest way to confirm auth actually worked end-to-end: if
// you can see your own claims, the token was validated.
[ApiController]
[Route("api/secure")]
[Authorize]
public class SecureController : ControllerBase
{
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value });
        return Ok(new { authenticated = User.Identity?.IsAuthenticated ?? false, claims });
    }
}
