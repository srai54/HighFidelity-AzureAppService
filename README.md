# HighFidelity Azure App Service — Study Repo

A local-only study/interview-prep project (**not pushed to any remote — this repo lives on this machine only**), sitting alongside `HighFidelity-Api` and `HighFidelity-Ui` as a third folder in the same workspace. Its job isn't to be a product — it's real, running, verified code plus plain-English notes for five Azure concepts that come up constantly in backend/cloud interviews:

1. **Key Vault + IOptions** — secrets management, injected via the options pattern
2. **Application Insights** — telemetry/observability
3. **Function Apps** — HTTP, Timer, and event (Service Bus) triggers
4. **Service Bus** — a queue, with a publisher and a receiver
5. **Blob Storage** — the three blob types (Block, Append, Page)

## Structure

```
src/
  WebApp/       → ASP.NET Core Web API — the "App Service" — Key Vault, App Insights, Blob Storage, Service Bus publisher
  Functions/    → Azure Functions (isolated worker) — HTTP trigger, Timer trigger, Service Bus trigger (the receiver)
docs/
  ARCHITECTURE.md          → how the two projects fit together, what's real vs. reference-only, honestly
  KEYVAULT.md              → theory + interview Q&A
  APPLICATION_INSIGHTS.md  → theory + interview Q&A
  FUNCTION_APPS.md         → theory + interview Q&A
  SERVICE_BUS.md           → theory + interview Q&A
  BLOB_STORAGE.md          → theory + interview Q&A
```

Each `docs/*.md` file follows the same shape: a plain-English explanation (with an analogy, aimed at actually sticking in memory rather than reading like a spec), how this repo implements it with real file references, and a set of interview questions with real answers at the end.

## Read this before trusting any "it works" claim

**Not everything here was run against a real Azure resource — this environment has no Azure subscription.** Rather than pretend otherwise, here's the honest split:

| Topic | Verified how |
|---|---|
| **Blob Storage** | ✅ Fully tested end-to-end against **Azurite** (Microsoft's local Storage emulator) — real upload/download for all three blob types, confirmed via curl |
| **Function Apps** | ✅ HTTP and Timer triggers run and tested locally via `func start` (Azure Functions Core Tools) — real HTTP calls, real timer fires observed in logs. ⚠️ The Service Bus-triggered function is written correctly but couldn't be exercised (no Service Bus connection available) — confirmed it fails to bind *gracefully*, without crashing the other two triggers |
| **Key Vault + IOptions** | ⚠️ The `IOptionsSnapshot`/`IOptionsMonitor` binding pattern is verified locally (falls through to `appsettings.Development.json`). The actual Key Vault call has not been made — no free local emulator exists for Key Vault, and no real Vault was available here |
| **Service Bus** | ⚠️ Publisher and receiver code is correct and matches the real SDK surface, but neither has sent/received an actual message — no local Service Bus emulator was available (it exists but needs Docker, which isn't installed here) |
| **Application Insights** | ⚠️ Verified the app starts correctly with no connection string configured (a real bug was hit and fixed here — see `docs/APPLICATION_INSIGHTS.md`). No telemetry has actually been shipped to a real Application Insights resource |

See `docs/ARCHITECTURE.md` for the full reasoning behind each of these.

## Running what's runnable locally

```powershell
# Blob Storage / general WebApp testing — needs Azurite
npm install -g azurite      # one-time
azurite --silent --skipApiVersionCheck --location <some-folder>

cd src/WebApp
dotnet run
# -> http://localhost:5000/swagger (or whatever port launchSettings.json picks)

# Functions — HTTP and Timer triggers work with no setup beyond Core Tools
npm install -g azure-functions-core-tools@4 --unsafe-perm true   # one-time
cd src/Functions
func start
# -> http://localhost:7071/api/greet/{name}
```

Key Vault and Service Bus need real Azure resources (a Vault + Managed Identity; a Service Bus namespace + connection string) to actually exercise — see each topic's doc for exactly what config keys to set.
