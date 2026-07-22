# Azure Key Vault + IOptions

## The plain-English version

Every app has secrets — a database password, a third-party API key, a signing key. You can't put those in `appsettings.json` because that file gets checked into git, and git history is forever. Key Vault is just **a locked box in Azure that holds secrets**, and your app is handed a key to that box (not a password — an *identity*) so it can ask "give me the secret named X" at startup.

The "inject using IOptions" part is a separate, older idea that Key Vault plugs into: instead of every class in your app calling `Configuration["PaymentGateway:ApiKey"]` directly (stringly-typed, easy to typo, hard to test), you define a plain C# class shaped like your config, bind it once, and every class that needs it just asks for that class via constructor injection. **Key Vault decides *where the value comes from*. IOptions decides *how your code receives it*. They don't know about each other** — that's the whole trick.

## The analogy that makes it click

Think of `IConfiguration` as a big shared dictionary that many different sources are allowed to add entries to — `appsettings.json` adds some, environment variables add some, and (if you turn it on) Key Vault adds some too, layered on top like sticky notes on a whiteboard, last one stuck wins. Your C# class (`PaymentGatewayOptions`) doesn't care which sticky note a value came from. It just says "give me everything under the `PaymentGateway` heading" and gets handed a fully-filled-in object.

## What you need to install (NuGet packages)

Wiring Key Vault into config needs **two** packages, and it's worth being clear on why it's two and not one — this is a common point of confusion:

```xml
<!-- The configuration provider: adds "read secrets from a Vault into
     IConfiguration" — i.e. the builder.Configuration.AddAzureKeyVault(...)
     extension method itself lives here. -->
<PackageReference Include="Azure.Extensions.AspNetCore.Configuration.Secrets" Version="1.5.1" />

<!-- The credential/auth library: provides DefaultAzureCredential (and the
     whole credential chain — Managed Identity, az login, Visual Studio, etc.).
     AddAzureKeyVault needs a credential passed to it, and this is where that
     type comes from. -->
<PackageReference Include="Azure.Identity" Version="1.21.0" />
```

Install them from the CLI (run in `src/WebApp`):
```powershell
dotnet add package Azure.Extensions.AspNetCore.Configuration.Secrets
dotnet add package Azure.Identity
```

**Why two packages?** They do genuinely separate jobs, and the split mirrors the "Key Vault decides *where* the value comes from; how you authenticate is a separate concern" idea:
- `Azure.Extensions.AspNetCore.Configuration.Secrets` = the **configuration provider** — it's what teaches `IConfiguration` to pull secrets from a Vault and merge them in (the `AddAzureKeyVault` extension method). Without it, that method doesn't exist.
- `Azure.Identity` = the **credential** — it supplies `DefaultAzureCredential`, the *identity* the provider uses to prove it's allowed to read the Vault. Without it, you'd have the ability to read a Vault but no way to authenticate to one.

A third package, `Azure.Security.KeyVault.Secrets` (v4.11.0), is also referenced in this repo — that's the lower-level SDK for talking to Key Vault *directly* (e.g. `new SecretClient(...).GetSecretAsync(...)` in code, outside the config system). You don't need it just for the `IConfiguration` integration, but it's handy if you ever want to read/write a secret imperatively rather than through config binding. The `using Azure.Identity;` at the top of `Program.cs` is what makes `DefaultAzureCredential` resolve.

## Sample `appsettings.json` (with Azure Key Vault integrated)

The trick to remember: **the app never puts secrets in `appsettings.json`.** The only Key-Vault-related thing that goes in config is the *pointer* to the Vault (`KeyVault:Uri`) — a non-secret URL. The actual secrets (`ApiKey`, `ApiSecret`) get *layered in at runtime* by the Key Vault provider, so config files only ever hold the non-secret `BaseUrl`.

**`appsettings.json`** — committed to git, only non-secrets, plus the Vault pointer:
```jsonc
{
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "AllowedHosts": "*",

  // The pointer to the Vault. This is NOT a secret — it's just a URL — so it's
  // safe to commit. When set, Program.cs calls AddAzureKeyVault against it;
  // when empty/absent, the whole Key Vault block is skipped and secrets come
  // from appsettings.Development.json / User Secrets instead.
  "KeyVault": {
    "Uri": "https://kv-highfid-12345.vault.azure.net/"
  },

  "PaymentGateway": {
    // Non-secret: safe here. ApiKey/ApiSecret are deliberately ABSENT — they
    // arrive from Key Vault at runtime (secret names "PaymentGateway--ApiKey"
    // and "PaymentGateway--ApiSecret", the "--" mapping to ":").
    "BaseUrl": "https://api.example-payments.test"
  }
}
```

**`appsettings.Development.json`** — for local dev, where there's no Vault; the *fake* secrets live here (and this file is typically git-ignored or holds only throwaway values). `KeyVault:Uri` is intentionally omitted so the Vault call is skipped entirely locally:
```jsonc
{
  "PaymentGateway": {
    "ApiKey": "local-dev-fake-key-not-a-real-secret",
    "ApiSecret": "local-dev-fake-secret-not-a-real-secret"
  }
}
```

**In Azure (App Service)**, you don't edit `appsettings.json` at all — you set `KeyVault__Uri` as an *application setting* (double-underscore = `:`), which overrides/supplies the config key without touching the committed file. See `scripts/03-app-service.azcli` for the exact command, and `scripts/01-keyvault.azcli` for creating the Vault and secrets those keys resolve to.

The precedence, worth committing to memory: `appsettings.json` (base) → `appsettings.{Environment}.json` → environment variables / App Service settings → **Key Vault (added last in `Program.cs`, so it wins)**. Same `PaymentGateway:ApiKey` key in two places → the later source wins.

## How this repo implements it

- **`src/WebApp/Configuration/PaymentGatewayOptions.cs`** — the plain class: `BaseUrl`, `ApiKey`, `ApiSecret`.
- **`src/WebApp/Program.cs`** — the conditional wiring:
  ```csharp
  var keyVaultUri = builder.Configuration["KeyVault:Uri"];
  if (!string.IsNullOrWhiteSpace(keyVaultUri))
  {
      builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
  }
  builder.Services.Configure<PaymentGatewayOptions>(builder.Configuration.GetSection(PaymentGatewayOptions.SectionName));
  ```
  Only reaches out to a real Key Vault when `KeyVault:Uri` is actually set (an App Service application setting in Azure). Locally, with no URI configured, that whole block is skipped, and the same `PaymentGateway:ApiKey`/`ApiSecret` keys are just read from `appsettings.Development.json` instead. **The binding code below it never changes.** That's the entire point of the pattern — swap the source, not the consumer.
- **`src/WebApp/Controllers/PaymentGatewayController.cs`** — the consumer side, and it demonstrates the *other* thing worth remembering: there are three flavors of "give me my options," and they're not interchangeable:

  | | Lifetime | Re-reads on config change? | Use when |
  |---|---|---|---|
  | `IOptions<T>` | Singleton | No — bound once at startup, frozen forever | Values that truly never change while the app runs |
  | `IOptionsSnapshot<T>` | Scoped | Yes — recomputed once per request | Most normal per-request usage; the "just give me the current value" default |
  | `IOptionsMonitor<T>` | Singleton | Yes — `.CurrentValue` is always fresh, plus an `OnChange` event | You're *inside* a singleton service and can't take a scoped dependency, but still want live-reloading config |

  This is the single most commonly-missed detail about the whole pattern — most tutorials only ever show `IOptions<T>` and never explain why the other two exist.

## Why `DefaultAzureCredential` and not a client secret

`DefaultAzureCredential` tries a chain of credential sources in order — environment variables, then Managed Identity (only succeeds when actually running inside Azure), then a handful of local developer credentials (Azure CLI login, Visual Studio, etc.) — and uses whichever one works. The payoff: **the exact same line of code authenticates locally (via your `az login` session) and in Azure (via Managed Identity)**, with zero secret of its own ever stored anywhere. That's not just convenient — it deletes an entire category of "the credential that authenticates you to Key Vault" secret that would otherwise itself need to be secured.

## Gotcha worth remembering

Key Vault secret names can't contain `:` (colons aren't a valid character in a secret name), but .NET config sections are colon-separated (`PaymentGateway:ApiKey`). The Key Vault configuration provider handles this by convention: a secret literally named `PaymentGateway--ApiKey` (double-dash) gets mapped to the config key `PaymentGateway:ApiKey` automatically. Miss this and you'll create a secret in the portal, restart the app, and the value will just silently not bind — no error, just an empty string.

## What's real vs. reference-only in this repo

This code is correct and would work as-is against a real Azure Key Vault with a Managed Identity assigned. It has **not** been run against a real Vault in this environment — there's no free local emulator for Key Vault (unlike Blob Storage's Azurite), and no Azure subscription available here. What *is* verified: the `IOptionsSnapshot`/`IOptionsMonitor` binding itself, tested locally by falling through to `appsettings.Development.json` (see `docs/ARCHITECTURE.md` for exactly what was and wasn't run).

---

## Interview Questions

**Q: What problem does Key Vault actually solve?**
Keeping secrets out of source control and config files, and out of environment variables set by hand on a server (which nobody audits or rotates). It centralizes secrets in one place with access control, audit logging, and rotation support — one Vault can back multiple apps.

**Q: Walk me through what happens when the app starts, in order.**
`builder.Configuration.AddAzureKeyVault(uri, credential)` adds Key Vault as a configuration provider. `DefaultAzureCredential` resolves an identity (Managed Identity in Azure). The provider fetches every secret in the Vault the identity has `get`/`list` permission on and merges them into `IConfiguration`, converting `--` to `:` in names. Anything bound via `Configure<T>()` afterward sees those values as if they'd always been in `appsettings.json`.

**Q: Why IOptions instead of just injecting IConfiguration everywhere?**
Strong typing (compiler catches a typo'd property name; it can't catch a typo'd string key like `"PaymentGatewya:ApiKey"`), one bound object instead of scattered `Configuration["..."]` calls, and testability — you can construct a `PaymentGatewayOptions` directly in a unit test and wrap it in `Options.Create(...)` without touching `IConfiguration` at all.

**Q: What's the actual difference between IOptions, IOptionsSnapshot, and IOptionsMonitor?**
`IOptions<T>` binds once at startup and never changes — it's a singleton. `IOptionsSnapshot<T>` is scoped and recomputes from current config once per request/scope — the normal choice. `IOptionsMonitor<T>` is a singleton but still re-reads live via `.CurrentValue`, plus it exposes an `OnChange` callback — needed specifically when a *singleton* service (which can't depend on a scoped `IOptionsSnapshot`) still needs config that can change without a restart.

**Q: If Key Vault has the value AND appsettings.json has the same key, which wins?**
Whichever config source was added *last* wins, by ASP.NET Core configuration convention (later sources override earlier ones for the same key). In this repo's `Program.cs`, `AddAzureKeyVault` is called after the default host configuration is built, so Key Vault values win over `appsettings.json` for any overlapping key.

**Q: Why DefaultAzureCredential instead of a client ID/secret?**
No credential to store, rotate, or leak — it uses Managed Identity in Azure (an identity tied to the resource itself, with no password to steal) and falls back to your local developer login for local dev. A client secret is just another secret needing its own secret-management story, which defeats some of the point of using Key Vault in the first place.

**Q: Which NuGet packages do you need to integrate Key Vault with IConfiguration, and why more than one?**
Two: `Azure.Extensions.AspNetCore.Configuration.Secrets` (the configuration provider — it's what adds the `AddAzureKeyVault` method that pulls Vault secrets into `IConfiguration`) and `Azure.Identity` (supplies `DefaultAzureCredential` — the identity used to authenticate to the Vault). They're separate because reading secrets *into config* and *authenticating* to the Vault are separate concerns — the provider needs a credential handed to it, and that credential type lives in the identity package. (`Azure.Security.KeyVault.Secrets` is a third, optional one — only needed if you want to read/write secrets imperatively in code via `SecretClient`, not through the config system.)

**Q: What actually goes in appsettings.json when you use Key Vault — do the secrets live there?**
No — the secrets never touch `appsettings.json`. The only Key-Vault-related thing in config is the non-secret *pointer* to the Vault (`KeyVault:Uri`, just a URL). The secrets are layered into `IConfiguration` at runtime by the Key Vault provider, so committed config files only ever hold non-secret values like `BaseUrl`. Locally, fake secrets go in `appsettings.Development.json` (with `KeyVault:Uri` omitted so no real Vault call happens); in Azure, `KeyVault__Uri` is set as an App Service application setting rather than edited into the file.

**Q: What would you change to make this production-grade?**
Add `ReloadInterval` to `AddAzureKeyVault` so rotated secrets get picked up without a restart; scope the Managed Identity's Vault access to `get`/`list` only (not `set`/`delete`) via Key Vault's RBAC or access policies; and use `IOptionsMonitor` anywhere a secret might rotate while the app is running, so the app doesn't need restarting to pick up a rotated key.
