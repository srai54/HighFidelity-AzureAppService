# Azure Key Vault + IOptions

## The plain-English version

Every app has secrets — a database password, a third-party API key, a signing key. You can't put those in `appsettings.json` because that file gets checked into git, and git history is forever. Key Vault is just **a locked box in Azure that holds secrets**, and your app is handed a key to that box (not a password — an *identity*) so it can ask "give me the secret named X" at startup.

The "inject using IOptions" part is a separate, older idea that Key Vault plugs into: instead of every class in your app calling `Configuration["PaymentGateway:ApiKey"]` directly (stringly-typed, easy to typo, hard to test), you define a plain C# class shaped like your config, bind it once, and every class that needs it just asks for that class via constructor injection. **Key Vault decides *where the value comes from*. IOptions decides *how your code receives it*. They don't know about each other** — that's the whole trick.

## The analogy that makes it click

Think of `IConfiguration` as a big shared dictionary that many different sources are allowed to add entries to — `appsettings.json` adds some, environment variables add some, and (if you turn it on) Key Vault adds some too, layered on top like sticky notes on a whiteboard, last one stuck wins. Your C# class (`PaymentGatewayOptions`) doesn't care which sticky note a value came from. It just says "give me everything under the `PaymentGateway` heading" and gets handed a fully-filled-in object.

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

**Q: What would you change to make this production-grade?**
Add `ReloadInterval` to `AddAzureKeyVault` so rotated secrets get picked up without a restart; scope the Managed Identity's Vault access to `get`/`list` only (not `set`/`delete`) via Key Vault's RBAC or access policies; and use `IOptionsMonitor` anywhere a secret might rotate while the app is running, so the app doesn't need restarting to pick up a rotated key.
