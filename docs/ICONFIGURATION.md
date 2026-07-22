# IConfiguration, Configuration Providers & the Middleware Pipeline

## The plain-English version

`IConfiguration` is **one merged dictionary of settings** that your whole app reads from. The clever part isn't the dictionary — it's that many different *sources* feed into it, layered on top of each other, and your code doesn't care which source a value came from. It just asks for `"PaymentGateway:BaseUrl"` and gets whatever the winning layer says.

Those sources are called **configuration providers**, and `WebApplication.CreateBuilder(args)` sets up a default stack of them for you, in this order (**later wins** for any key that appears in more than one):

1. `appsettings.json`
2. `appsettings.{Environment}.json` (e.g. `appsettings.Development.json`)
3. User Secrets (Development only)
4. Environment variables (this is where **App Service application settings** land in Azure)
5. Command-line arguments

You can add more with `builder.Configuration.Add*(...)`. Key Vault (`AddAzureKeyVault`) is just another provider added to this stack — added *last* in this repo's `Program.cs`, so its values win. This is the single mental model that ties Key Vault, appsettings, and environment variables together: **they're all just layers in the same stack.**

## The analogy that makes it click

Think of a stack of transparent sheets on an overhead projector. `appsettings.json` is the bottom sheet with most values written on it. Each additional provider is another transparent sheet laid on top — environment variables, then Key Vault. When you look down through the stack at a given spot (a config key), you see whatever's written on the *topmost* sheet that has something there. Your code is the person reading from above; it never knows or cares which sheet a value was actually written on.

## Two ways to read config in your code

Both read from the exact same merged `IConfiguration`. They differ only in *how your code receives the values*:

| | How | Trade-off | This repo |
|---|---|---|---|
| **Direct `IConfiguration`** | Inject `IConfiguration`, read `config["Section:Key"]` | Quick, flexible, but **stringly-typed** — a typo'd key compiles fine and returns null at runtime; no strong typing | `ConfigDemoController` |
| **`IOptions<T>` pattern** | Bind a section to a C# class once, inject `IOptions<T>`/`IOptionsSnapshot<T>`/`IOptionsMonitor<T>` | Strongly-typed, testable, catches typos at compile time; a bit more setup | `PaymentGatewayController` (see `docs/KEYVAULT.md`) |

Rule of thumb: **`IOptions<T>` for anything real** (it's type-safe and testable); direct `IConfiguration` for one-off reads, or when you genuinely need the raw provider machinery (like reading `KeyVault:Uri` in `Program.cs` *before* the DI container even exists).

## How this repo implements it

**`src/WebApp/Program.cs`** — an explicit extra provider, purely to make the "stack of sources" idea visible:
```csharp
builder.Configuration.AddJsonFile("appsettings.custom.json", optional: true, reloadOnChange: true);
```
- `optional: true` → the app still starts if the file doesn't exist.
- `reloadOnChange: true` → edits to the file are picked up live, without a restart (this is the machinery `IOptionsSnapshot`/`IOptionsMonitor` rely on to serve fresh values — see `docs/KEYVAULT.md`).

**`src/WebApp/Controllers/ConfigDemoController.cs`** — reading config directly, three ways:
- `GET /api/config-demo/value` — a single value (`config["PaymentGateway:BaseUrl"]`) and a typed-with-default read (`config.GetValue<int>("PaymentGateway:ReloadSeconds", 30)`).
- `GET /api/config-demo/section` — bind a whole section to an object in one shot with `.GetSection("PaymentGateway").Get<T>()` (secrets masked).
- `GET /api/config-demo/providers` — dumps the actual ordered provider list from `IConfigurationRoot`, so you can *see* the layering (earliest → latest = lowest → highest priority). The fastest way to debug "why is this value not what appsettings.json says" (usually: an env var or Key Vault layer above it wins).

## The middleware pipeline & `UseStaticFiles()`

Separate from configuration, but the other half of what a `Program.cs` sets up: the **middleware pipeline**. After `var app = builder.Build()`, each `app.Use*(...)` adds a middleware that runs *in order*, each one wrapping the next like layers of an onion around the eventual endpoint.

**`app.UseStaticFiles()`** serves files straight from the `wwwroot/` folder — `wwwroot/index.html` becomes `GET /index.html` — **without touching a controller, routing, or MVC**. When a request path matches a file on disk, this middleware returns it and *short-circuits* the rest of the pipeline. That's exactly why it's placed **early** (before routing/auth): there's no reason to run authentication or controller routing for a static image or HTML file.

This repo:
- **`src/WebApp/Program.cs`** — `app.UseStaticFiles();` added near the top of the pipeline.
- **`src/WebApp/wwwroot/index.html`** — a sample static file. Because the app root `/` is redirected to `/swagger`, open **`/index.html`** explicitly to see it served.

Pipeline order in this repo (simplified): `UseStaticFiles` → `UseSwagger`/`UseSwaggerUI` → (`UseAuthentication` if Entra ID configured) → `UseAuthorization` → `MapControllers`. Order is not cosmetic — e.g. `UseAuthentication` must come before `UseAuthorization` (you can't authorize an identity you haven't established yet).

## What's real vs. reference-only in this repo

The code compiles cleanly. As with the other recent additions (see `docs/ARCHITECTURE.md`), the WebApp couldn't be *run* this session due to the machine's Smart App Control restriction — but the `IConfiguration` reads, `AddJsonFile` provider, and `UseStaticFiles` middleware are all completely standard ASP.NET Core with no external dependency, so they'd work as-is under `dotnet run` on an unrestricted machine.

---

## Interview Questions

**Q: What is IConfiguration and where do its values come from?**
It's a single merged view of settings from multiple *configuration providers* layered in order — appsettings.json, environment-specific appsettings, User Secrets, environment variables, command-line args by default, plus anything you add (like Key Vault). Later providers override earlier ones for the same key. Code reads from the merged result and doesn't know which provider supplied a given value.

**Q: If the same key is in appsettings.json and an environment variable, which wins?**
The environment variable — it's registered *after* appsettings.json in the default provider order, and later providers win. This is exactly how App Service application settings override committed appsettings.json values in Azure without changing the file.

**Q: When would you inject IConfiguration directly vs. use IOptions<T>?**
Use `IOptions<T>` for real, structured settings — it's strongly-typed (compiler catches key typos), testable, and supports reload via `IOptionsSnapshot`/`IOptionsMonitor`. Inject `IConfiguration` directly for quick one-off reads, or when you need config *before* DI exists (e.g. in `Program.cs` startup logic), where there's no bound options object yet.

**Q: What does `reloadOnChange: true` on a JSON provider actually do?**
It watches the file and re-reads it into `IConfiguration` when it changes on disk — no app restart needed. Combined with `IOptionsSnapshot`/`IOptionsMonitor`, that means a running app can pick up an edited setting live. (`IOptions<T>`, bound once at startup, would *not* see the change.)

**Q: What is middleware, and why does order matter?**
Middleware are components that form a pipeline each HTTP request passes through, in the order registered, each wrapping the next. Order matters because they can short-circuit (static files returns a file and stops) or depend on each other (`UseAuthentication` must run before `UseAuthorization`, since you can't authorize an identity that hasn't been established). Put cheap short-circuiting middleware (static files) early and endpoint execution (`MapControllers`) last.

**Q: What does UseStaticFiles() do and where should it sit in the pipeline?**
It serves files directly from `wwwroot/` when the request path matches a file, bypassing routing/controllers entirely and short-circuiting the rest of the pipeline. It belongs early — before auth and routing — because serving a static asset shouldn't incur authentication or controller-resolution work.
