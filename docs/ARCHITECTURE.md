# Architecture — How the Pieces Fit Together

## The two projects, and why there are two

**`src/WebApp`** is what would deploy to an **Azure App Service** — an always-on ASP.NET Core process, listening on a port, handling HTTP requests. It owns four of the five topics: Key Vault/IOptions, Application Insights, Blob Storage, and the Service Bus *publisher*.

**`src/Functions`** is what would deploy to an **Azure Function App** — no always-on process, just code that wakes up when its trigger fires. It owns the fifth topic (three trigger types: HTTP, Timer, event) and, specifically, the Service Bus *receiver* — the other half of the publisher in `WebApp`.

They're deliberately separate projects, not one big app, for the same reason `HighFidelity-Api` and `HighFidelity-Ui` are separate repos: these are two independently deployable Azure resources in real life, with their own scaling behavior, their own billing model, their own release cadence. `OrderCreatedMessage` is defined twice (once per project) rather than shared via a project reference — see `docs/SERVICE_BUS.md` for why that duplication is deliberate, not an oversight.

## What "Azure App Service" actually means, briefly

App Service is Azure's managed hosting for web apps — you give it a deployable (a zip, a container, or CI/CD from a repo) and it runs it, handles the OS/runtime patching, and gives you scaling, custom domains, TLS, and slots without you managing a VM. The concepts in this repo are the things that *commonly get bolted onto* an App Service, not App Service itself:
- **Key Vault** — because secrets shouldn't be App Service application settings typed in by hand forever (they can be, but Key Vault is the better answer past a certain team size/secret count).
- **Application Insights** — because you can't attach a debugger to a deployed App Service; Insights is how you see what's actually happening.
- **Functions / Service Bus / Blob Storage** — because a real backend is rarely *just* a web app; it's a web app plus background work (Functions), decoupled processing (Service Bus), and file storage (Blob Storage), often across multiple Azure resources that only talk to each other over well-defined boundaries (HTTP, a queue, a storage account) — never a shared in-process call.

## The one thing that actually connects the two projects

`OrdersController.CreateOrder` (WebApp) publishes to the `orders` Service Bus queue. `OrderCreatedFunction` (Functions) is triggered by messages landing in that same queue. That's the *only* connection between the two projects — no shared database, no shared assembly, no direct network call from one to the other. This is intentional and is exactly the pattern real distributed Azure systems use: services communicate through a message broker or well-defined HTTP contract, never by reaching into each other's internals.

## Being honest about what's verified

This repo was built without access to a real Azure subscription. Rather than write code that merely *looks* plausible, each topic was tested as far as it's actually possible to test locally, and no further — and that boundary is worth understanding because it maps onto a real skill: knowing what you *can* verify without cloud access versus what genuinely requires it.

**Fully verified, end-to-end, against a real local server (not a mock):**
- **Blob Storage** — Azurite (`npm install -g azurite`) is Microsoft's own local Storage emulator; it speaks the same REST API as real Azure Storage. `BlobServiceClient` pointed at `"UseDevelopmentStorage=true"` is *the same SDK code* talking to a local process instead of Azure — not a test double, not a mock, the real client. All three blob types were uploaded, listed, and downloaded successfully.
- **HTTP and Timer Function triggers** — Azure Functions Core Tools (`func start`) runs the actual Functions host locally. The HTTP trigger was called with curl and returned the real response. The Timer trigger's schedule was temporarily shortened to 10 seconds, confirmed firing three times in the host log, then reverted to the real 5-minute schedule before committing.

**Partially verified — the code path was exercised, but not the live cloud call:**
- **Application Insights** — `AddApplicationInsightsTelemetry()` was confirmed to *throw at startup* with no connection string (a real bug, not a hypothetical — see `docs/APPLICATION_INSIGHTS.md`), and the fix (a conditional guard) was confirmed to let the app start cleanly instead. No telemetry has actually reached a real Application Insights resource.
- **Key Vault** — the `IOptionsSnapshot`/`IOptionsMonitor` binding was confirmed working, backed by `appsettings.Development.json` standing in for what Key Vault would provide. The actual `AddAzureKeyVault(...)` call, against a real Vault with `DefaultAzureCredential`, has not been exercised.
- **Service Bus-triggered Function** — confirmed it fails to *start listening* without a real connection string, and confirmed that failure doesn't crash the other two triggers in the same host. It has never actually received a message.

**Written correctly, not run at all:**
- **Service Bus publisher** (`ServiceBusPublisher.cs`) — matches the real `Azure.Messaging.ServiceBus` SDK surface, but no message has ever actually been sent, because there's no local Service Bus emulator available here without Docker (Microsoft does publish one, Docker-based — just not installed in this environment).

If you're presenting this repo to someone, the honest framing is: **"Blob Storage and two of the three Function trigger types are genuinely tested locally. Key Vault, Application Insights, and Service Bus are correct reference implementations I haven't been able to run against real Azure resources."** That's a materially more credible claim than pretending everything was verified end-to-end, and it's the kind of distinction an interviewer asking follow-up questions will respect more than a confident-sounding claim that falls apart under one pointed question.

## The extra topics added afterward, and a new blocker

A second round added seven more topics interviewers commonly ask about: Resilience/Polly, Durable Functions, Managed Identity + Entra ID, Redis Cache, Azure SQL vs. Cosmos DB, a Service Bus/Event Grid/Storage Queues/Event Hub comparison, and App Service deployment slots/scaling. Each has its own doc under `docs/` with the same structure as the original five.

Partway through that round, this machine's Windows **Smart App Control** policy started blocking `dotnet run` and `func start` from loading their own freshly-built binaries — confirmed via the CodeIntegrity event log (`Microsoft-Windows-CodeIntegrity/Operational`, event IDs 3077/3033): *"Code Integrity determined that a process (...WebApp.exe) attempted to load (...WebApp.dll) that did not meet the Enterprise signing level requirements."* Identical failure on the Functions side against `Functions.dll`. This is a machine security policy evaluating unsigned local dev builds, not a bug introduced by any of the new code — every new addition still builds with 0 errors/warnings. It just means none of it could be exercised at runtime this session, the way Blob Storage and the HTTP/Timer triggers were earlier. The one exception: Redis's cache-aside logic is genuinely exercised against `IDistributedCache`'s in-memory fallback implementation, since that's a real (non-mocked) implementation of the same interface Redis uses — it's specifically the *Redis* backend, and everything gated behind actually running the WebApp process, that's unverified.

If you're picking this up later on a machine without that restriction, `docs/RESILIENCE_POLLY.md` and `docs/REDIS_CACHE.md` describe the fastest things to try first — both work with zero external dependencies beyond `dotnet run`.

## Config keys, all in one place

| Key | Where it's read | Required for |
|---|---|---|
| `KeyVault:Uri` | `WebApp/Program.cs` | Enabling real Key Vault (absent = falls back to local config) |
| `PaymentGateway:BaseUrl/ApiKey/ApiSecret` | `WebApp/Configuration/PaymentGatewayOptions.cs` | The IOptions demo (BaseUrl in `appsettings.json`; ApiKey/ApiSecret in `appsettings.Development.json` locally, or Key Vault in Azure) |
| `ApplicationInsights:ConnectionString` (or `APPLICATIONINSIGHTS_CONNECTION_STRING`) | `WebApp/Program.cs` | Enabling telemetry (absent = feature disabled, not crashed — after the fix) |
| `Storage:ConnectionString` | `WebApp/Program.cs` | Blob Storage (defaults to Azurite's `UseDevelopmentStorage=true`) |
| `ServiceBus:ConnectionString` | `WebApp/Program.cs` | Enabling the publisher (absent = `IServiceBusPublisher` isn't registered, `OrdersController` fails to resolve) |
| `ServiceBusConnection` (app setting name, not the string itself) | `Functions/local.settings.json` / Function App settings | The Service Bus-triggered function actually listening |
| `AzureAd:TenantId` / `:Instance` / `:ClientId` | `WebApp/Program.cs` | Enabling real Entra ID JWT bearer validation (absent = no auth scheme registered, `[Authorize]` endpoints have nothing to validate against) |
| `Redis:ConnectionString` | `WebApp/Program.cs` | Using real Azure Cache for Redis (absent = falls back to `AddDistributedMemoryCache`, same `IDistributedCache` interface) |
| `Cosmos:ConnectionString` | `WebApp/Program.cs` | Enabling the `CosmosClient` registration (absent = `CosmosDemoController` returns 503 rather than throwing on missing DI) |
| `ResilienceDemo:TargetBaseUrl` | `WebApp/Program.cs` | Where the `ResilientClient` named HttpClient points — defaults to this app's own `http://localhost:5175`, no config needed for the demo to work |
