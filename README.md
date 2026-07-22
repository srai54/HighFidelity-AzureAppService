# HighFidelity Azure App Service — Study Repo

A local-only study/interview-prep project (**not pushed to any remote — this repo lives on this machine only**, tracked instead on GitHub in its own repo/branch — see below), sitting alongside `HighFidelity-Api` and `HighFidelity-Ui` as a third folder in the same workspace. Its job isn't to be a product — it's real, working code plus plain-English notes for the Azure concepts that come up constantly in backend/cloud interviews.

**Note:** the section header above is historical — this repo was later pushed to `github.com/srai54/HighFidelity-AzureAppService` on branch `azureappserviceinitial`, at the user's explicit request, once they'd decided they wanted a backup/reference copy off this machine.

The original five:
1. **Key Vault + IOptions** — secrets management, injected via the options pattern
2. **Application Insights** — telemetry/observability
3. **Function Apps** — HTTP, Timer, and event (Service Bus) triggers
4. **Service Bus** — a queue, with a publisher and a receiver
5. **Blob Storage** — the three blob types (Block, Append, Page)

Plus the extra topics that round out a typical Azure backend interview:

6. **Resilience (Polly)** — retry, circuit breaker, timeout on outbound HTTP calls
7. **Durable Functions** — stateful orchestration (fan-out/fan-in, chaining) on top of Functions
8. **Managed Identity + Entra ID** — how the app authenticates *outbound* to Azure (Managed Identity) vs. how it validates *inbound* callers (Entra ID/JWT bearer)
9. **Redis Cache** — the cache-aside pattern via `IDistributedCache`
10. **Azure SQL vs. Cosmos DB** — relational vs. NoSQL, and why partition keys matter
11. **Messaging comparison** — Service Bus vs. Event Grid vs. Storage Queues vs. Event Hub, when to use which
12. **App Service deployment slots & scaling** — blue-green swaps, scale up vs. scale out

## Structure

```
src/
  WebApp/       → ASP.NET Core Web API — the "App Service" — Key Vault, App Insights, Blob Storage, Service Bus publisher,
                  Resilience/Polly, Managed Identity + Entra ID, Redis cache, Cosmos DB
  Functions/    → Azure Functions (isolated worker) — HTTP trigger, Timer trigger, Service Bus trigger (the receiver),
                  Durable Functions orchestration
docs/
  ARCHITECTURE.md              → how the two projects fit together, what's real vs. reference-only, honestly
  KEYVAULT.md                  → theory + interview Q&A
  APPLICATION_INSIGHTS.md      → theory + interview Q&A (+ fully-integrated sample code)
  APP_INSIGHTS_FUNCTIONS_DEMO.md → deploy a Function App + App Insights, check Live Metrics/Performance
  FUNCTION_APPS.md             → theory + interview Q&A
  SERVICE_BUS.md               → theory + interview Q&A
  SERVICE_BUS_PORTAL_GUIDE.md  → portal click-through: create namespace/queue, send/receive, connection strings
  SEND_TO_SERVICE_BUS_FROM_VISUAL_STUDIO.md → run the WebApp in VS and publish a message via Swagger
  SERVICE_BUS_SENDER_CONSOLE.md → minimal console sender from scratch (NuGet → client → sender → send)
  SERVICE_BUS_RECEIVER_CONSOLE.md → minimal console receiver (client → receiver → receive/complete)
  SERVICE_BUS_DEAD_LETTER_QUEUE.md → what a DLQ is, sending to it (dead-lettering), reading from it
  BLOB_STORAGE.md              → theory + interview Q&A
  RESILIENCE_POLLY.md          → theory + interview Q&A
  DURABLE_FUNCTIONS.md         → theory + interview Q&A
  MANAGED_IDENTITY_ENTRA_ID.md → theory + interview Q&A
  REDIS_CACHE.md               → theory + interview Q&A
  SQL_VS_COSMOS.md             → theory + interview Q&A
  MESSAGING_COMPARISON.md      → theory + interview Q&A (conceptual — no dedicated code)
  DEPLOYMENT_SLOTS_SCALING.md  → theory + interview Q&A (conceptual — no dedicated code)
  PROVISIONING.md              → how to CREATE the Azure resources (indexes the scripts/ folder)
  STUDY_RESOURCES.md           → external study links (videos/articles) collected per topic
scripts/
  00..09 + 99 .azcli           → commented az CLI provisioning scripts, one per feature (learning
                                 reference — NOT auto-run; you run them yourself after `az login`)
samples/
  ServiceBusSenderConsole/     → minimal standalone console app that sends one message to a queue
                                 (paired with docs/SERVICE_BUS_SENDER_CONSOLE.md)
  ServiceBusReceiverConsole/   → minimal console receiver (docs/SERVICE_BUS_RECEIVER_CONSOLE.md)
  ServiceBusDeadLetterConsole/ → dead-letter a message + read the DLQ (docs/SERVICE_BUS_DEAD_LETTER_QUEUE.md)
```

Each `docs/*.md` file follows the same shape: a plain-English explanation (with an analogy, aimed at actually sticking in memory rather than reading like a spec), how this repo implements it with real file references, and a set of interview questions with real answers at the end.

**To actually deploy any of this to Azure**, see `docs/PROVISIONING.md` — it indexes the `scripts/` folder (one commented az CLI script per feature), covers the run order, the app-settings wiring, and the cost breakdown against a free credit. Those scripts are a **learning reference only**: nothing in this repo executes them, and they create real billable resources solely when you choose to run them yourself.

## Read this before trusting any "it works" claim

**Not everything here was run against a real Azure resource — this environment has no Azure subscription.** Rather than pretend otherwise, here's the honest split:

| Topic | Verified how |
|---|---|
| **Blob Storage** | ✅ Fully tested end-to-end against **Azurite** (Microsoft's local Storage emulator) — real upload/download for all three blob types, confirmed via curl |
| **Function Apps** | ✅ HTTP and Timer triggers run and tested locally via `func start` (Azure Functions Core Tools) — real HTTP calls, real timer fires observed in logs. ⚠️ The Service Bus-triggered function is written correctly but couldn't be exercised (no Service Bus connection available) — confirmed it fails to bind *gracefully*, without crashing the other two triggers |
| **Key Vault + IOptions** | ⚠️ The `IOptionsSnapshot`/`IOptionsMonitor` binding pattern is verified locally (falls through to `appsettings.Development.json`). The actual Key Vault call has not been made — no free local emulator exists for Key Vault, and no real Vault was available here |
| **Service Bus** | ⚠️ Publisher and receiver code is correct and matches the real SDK surface, but neither has sent/received an actual message — no local Service Bus emulator was available (it exists but needs Docker, which isn't installed here) |
| **Application Insights** | ⚠️ Verified the app starts correctly with no connection string configured (a real bug was hit and fixed here — see `docs/APPLICATION_INSIGHTS.md`). No telemetry has actually been shipped to a real Application Insights resource |
| **Resilience (Polly)** | 🔴 Compiles cleanly, matches the real `Microsoft.Extensions.Http.Resilience` API. Could not be run — see the Smart App Control note below |
| **Durable Functions** | 🔴 Compiles cleanly, matches the real isolated-worker Durable Task API. Could not be run — same blocker |
| **Managed Identity + Entra ID** | ⚠️ Compiles cleanly. No real Entra ID tenant was available to validate a token against even without the runtime blocker |
| **Redis Cache** | ✅ The cache-aside pattern genuinely works against `IDistributedCache`'s in-memory implementation (a real, non-mocked backing store) — just not against an actual Redis instance (no Docker/local Redis here) |
| **Azure SQL vs. Cosmos DB** | ⚠️ Cosmos code compiles cleanly, matches the real SDK. No Cosmos DB Emulator or real account was available |
| **Messaging comparison / Deployment slots & scaling** | 📘 Conceptual only, by design — no dedicated code, these are "which one and why" and infrastructure-config topics |

**New this round — a machine-level blocker, not a code issue:** partway through adding the extra topics above, this machine's Windows **Smart App Control** policy started blocking `dotnet run`/`func start` from loading their own freshly-built binaries (confirmed via the CodeIntegrity event log — see `docs/ARCHITECTURE.md`). This affected both `src/WebApp` and `src/Functions` regardless of which feature was being tested, which is why several of the newer additions above are marked "compiles but couldn't run" even where the underlying pattern (like Redis's cache-aside logic) is otherwise simple to verify. It has nothing to do with any of the earlier ✅ results, which were captured in an earlier session before this restriction was in effect.

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

Key Vault, Service Bus, Cosmos DB, Redis, and Entra ID all need real Azure resources (or, for Redis/Cosmos, an emulator not installed in this environment) to actually exercise — see each topic's doc for exactly what config keys to set. The Resilience (`/api/resilience/...`) and Redis cache-aside (`/api/cache-demo/...`) demo endpoints need no external dependency at all beyond `dotnet run` itself — they're the two easiest things in this repo to try first if you're picking this back up on a machine without the Smart App Control restriction.
