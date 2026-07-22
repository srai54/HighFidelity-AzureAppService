# Managed Identity + Entra ID — Two Different Auth Directions

## The plain-English version

This topic actually covers **two separate problems** that get conflated a lot in interviews because they both end with "...and Azure handles the credentials for you":

1. **How does *this app* authenticate to *other Azure services*** (Key Vault, Storage, Service Bus) **without a secret sitting in config?** → **Managed Identity**, accessed through `DefaultAzureCredential`.
2. **How does *this app* verify that an incoming HTTP request is really from an authenticated user/client**, instead of trusting a username/password it manages itself? → **Entra ID** (formerly Azure AD) as an identity provider, validated via JWT bearer tokens.

Same underlying identity platform (Entra ID), two opposite directions of trust: one is this app *proving who it is* to Azure; the other is this app *checking who's calling it*.

### Managed Identity

Normally, if App A needs to call Service B, App A needs a secret — an API key, a connection string, a client secret — stored *somewhere*, and that "somewhere" is itself a problem (config file? Key Vault? but then how do you authenticate to Key Vault without a secret for that...). **Managed Identity** breaks the cycle: Azure itself gives your App Service/Function/VM an identity, backed by Entra ID, with **no credential your code ever sees or stores**. Azure's infrastructure vouches for the identity directly.

- **System-assigned** — the identity is tied to this one resource's lifecycle. Delete the App Service, the identity is gone with it. Simplest option; use it when only one resource needs the identity.
- **User-assigned** — the identity exists independently, as its own Azure resource, and can be attached to multiple App Services/Functions/VMs at once. Use it when several resources need to share the same permissions, or when you want the identity to outlive any single resource (e.g., swap which App Service backs a workload without redoing role assignments).

`DefaultAzureCredential` (already used in this repo's `Program.cs` for Key Vault — see `docs/KEYVAULT.md`) is the class that makes this invisible in code: it tries, in order, environment variables, then Managed Identity (this only succeeds when actually running on Azure infrastructure that has one assigned), then a chain of local developer credentials (Azure CLI login, Visual Studio, etc.). The same line of code authenticates correctly whether it's running on your laptop or in Azure, because each environment satisfies a different link in that chain.

### Entra ID as an identity provider for your API

Instead of your API rolling its own username/password + JWT signing (like the custom `AuthBusinessLogic`/`PasswordHasher`/HMAC-JWT setup in the sibling `HighFidelity-Api` repo), you register the API as an **app registration** in Entra ID. Clients authenticate against Entra ID directly (or via another app registration acting on a user's behalf) and get back a JWT. Your API's only job is to **validate** that token — check its signature against Entra ID's public keys, check the audience/issuer match your app registration, check it hasn't expired. You never see a password; Entra ID is the one authenticating the actual user/client.

## The analogy that makes it click

- **Managed Identity** is like having a building badge that just *works* at every door you're allowed through, issued and revoked centrally by building security (Azure), with no key you personally carry or could lose. System-assigned = a badge printed for this one employee's desk, destroyed when the desk is decommissioned. User-assigned = a master badge that exists independently and can be handed to whichever employee (resource) currently needs that access.
- **Entra ID validating your API's callers** is like a nightclub that doesn't check IDs itself — it trusts wristbands issued at the door by a security company (Entra ID) that already verified everyone. The bouncer at your API just checks "is this wristband real and not expired," never asks for a driver's license again.

## How this repo implements it

**Managed Identity (outbound)** — already present via `DefaultAzureCredential` in `src/WebApp/Program.cs` for Key Vault (`docs/KEYVAULT.md` covers this in depth); the identical credential chain would apply if Blob Storage or Service Bus were switched from connection strings to identity-based auth (`new BlobServiceClient(uri, new DefaultAzureCredential())` instead of a connection string) — not done here to keep the local/Azurite path simple, but it's a one-line change in Azure.

**Entra ID (inbound)** — `src/WebApp/Program.cs`:
```csharp
var azureAdTenantId = builder.Configuration["AzureAd:TenantId"];
if (!string.IsNullOrWhiteSpace(azureAdTenantId))
{
    builder.Services.AddAuthentication(Microsoft.Identity.Web.Constants.Bearer)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}
```
Conditional on a real tenant being configured — same shape as the Key Vault guard, for the same reason: nothing real to validate against locally without an actual Entra ID app registration. `src/WebApp/Controllers/SecureController.cs` exposes `GET /api/secure/whoami`, marked `[Authorize]`, which echoes back the caller's JWT claims — the simplest possible proof that authentication worked, because if you can see your own claims, the token was already validated by the time your code runs.

Required config shape (would go in `appsettings.Development.json` locally or app settings in Azure — not checked into this repo since it needs a real tenant/app registration to mean anything):
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your tenant ID>",
    "ClientId": "<this API's app registration client ID>"
  }
}
```

## What's real vs. reference-only in this repo

The code compiles and matches the real `Microsoft.Identity.Web` API surface (`AddMicrosoftIdentityWebApi`, the config section shape). **Nothing here was exercised against a real Entra ID tenant** — there's no free local emulator for Entra ID (unlike Azurite for Storage), and no real tenant/app registration was available in this environment. Combined with the Smart App Control restriction noted in `docs/ARCHITECTURE.md`, the app couldn't even be started locally this session to confirm it degrades gracefully with no `AzureAd:TenantId` set (the conditional guard mirrors the already-proven-correct Key Vault pattern, so the logic itself is sound, but that specific claim is unverified for this addition).

---

## Interview Questions

**Q: What's the actual difference between system-assigned and user-assigned managed identity?**
System-assigned is a 1:1 identity tied to a single resource's lifecycle — created when the resource is, destroyed when it is, can't be shared. User-assigned is a standalone Azure resource in its own right, independent of any single App Service/Function/VM, that can be attached to multiple resources simultaneously and outlives any one of them.

**Q: Why is Managed Identity considered more secure than a connection string or API key in config?**
There's no secret to leak, rotate, or accidentally commit to source control — the credential never exists in a form your code (or a config file, or an environment variable) holds directly. Azure's infrastructure vouches for the identity at the platform level. It also removes the "how do I securely store the credential I need to authenticate to the place that stores my other credentials" bootstrapping problem entirely.

**Q: How does `DefaultAzureCredential` decide which credential to actually use?**
It tries a fixed, ordered chain of credential sources and uses the first one that succeeds: environment variables, then Managed Identity (only viable when actually running on Azure infrastructure with one assigned), then a series of local developer credentials (Visual Studio, Azure CLI, Azure PowerShell, etc.). This is why the exact same line of code works unchanged on a developer's laptop and in a deployed Azure resource — different environments satisfy different links in the chain, and the code never needs to branch on where it's running.

**Q: If your API validates Entra ID tokens, does that replace the need for your own authorization logic?**
No — Entra ID (via JWT validation) answers "is this a real, unexpired, correctly-signed token issued for my app" (authentication). It doesn't answer "should *this specific* authenticated user be allowed to do *this specific* action" (authorization) — that's still your app's job, typically via role claims in the token combined with `[Authorize(Roles = "...")]` or custom policy handlers.

**Q: Why can't you just test Entra ID locally the way you tested Blob Storage with Azurite?**
Azurite works because Microsoft ships a genuine local reimplementation of the Storage REST API. There's no equivalent local reimplementation of Entra ID's token issuance and validation — it's tied to a real, centrally-managed tenant. The closest you can get locally is generating a token from a real (often free-tier/dev) tenant and testing validation against that, which is why this topic in particular tends to be the one interviewers expect you to explain conceptually rather than assume you've run end-to-end in a sandbox.

**Q: When would you choose custom JWT auth (like the HMAC-signed tokens in `HighFidelity-Api`) over Entra ID?**
Custom JWT makes sense for a small, self-contained system where you already own user management and don't need SSO, multi-tenant support, or integration with an organization's existing directory — it's simpler to reason about and has no external dependency. Entra ID makes sense once you need real enterprise identity features: SSO across multiple apps, MFA, conditional access policies, integration with an organization's existing user directory, or B2C scenarios — reinventing all of that yourself is a lot of security-sensitive surface area to maintain.
