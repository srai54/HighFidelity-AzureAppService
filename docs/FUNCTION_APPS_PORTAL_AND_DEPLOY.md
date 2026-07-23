# Azure Functions — Create, Publish, Test in Portal, URLs, Keys & Logs

The **practical / operational** companion to `docs/FUNCTION_APPS.md` (which covers the
*concepts* — triggers vs. bindings, plans, return-value semantics). This doc is the
"how do I actually create one, publish it, call it, secure it, and read its logs" walkthrough,
plus `local.settings.json`.

## What is an Azure Function? What is a Function App?

- **Azure Function** = a single piece of code that runs when its **one trigger** fires. It is
  *not* an always-running process — the Functions **host** is always there and invokes your
  method on demand, then it goes back to not running.
- **Azure Function App** = the **deployment + hosting container** for one or more functions.
  It's the top-level Azure resource (the thing you create, publish to, scale, and configure);
  the individual functions live *inside* it and share its app settings, plan, storage account,
  and Application Insights.

> Mental model: **Function App = the box (the process/host + config); Function = one item in the box.**
> This repo's `src/Functions/` project is one Function App containing several functions
> (`HttpTriggerFunction`, `TimerTriggerFunction`, `OrderCreatedFunction`, …).

### "One (HTTP) function ≈ one API endpoint"

For an **HTTP-triggered** function this is exactly right: each HTTP function is reached at its
own URL and behaves like one Web API endpoint. This repo's `HttpTriggerFunction` is
`[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "greet/{name}")]` → the single
endpoint `GET /api/greet/{name}`. Same request→work→response model as a Controller action;
the difference is **hosting and billing**, not the programming model (see `docs/FUNCTION_APPS.md`).

Caveat worth saying out loud in an interview: the "one function = one endpoint" rule only holds
for **HTTP** triggers. A Timer or Service Bus function has **no URL at all** — nobody calls it;
its trigger (a schedule / an arriving message) wakes it up.

## Containers, scaling & cost — why Functions are cost-effective

Under the hood the Functions host runs on **instances (containers)**, and the platform can
**add or remove instances automatically** based on load:

- **Consumption plan** — scales instances **out (and to zero)** on demand; you pay **per
  execution + per second of actual run time**, nothing while idle. This is the cost pitch:
  a nightly job or an occasional webhook doesn't pay for a 24/7 server.
- **Premium plan** — pre-warmed instances (no cold starts), VNet integration, longer runs.
- **Dedicated (App Service) plan** — runs on your existing App Service instances; useful when
  you already have spare capacity there.

So "**increase the number of containers**" = the platform spins up more instances to handle
more concurrent triggers, then scales back down — you don't manage servers, and you don't pay
for idle. This is the core commercial reason to choose Functions for bursty/event-driven work.

**Integrating with other services** is done through **triggers and bindings** — declaratively,
via an attribute, with the connection coming from an **app setting** (not hardcoded). One
function can be triggered by an HTTP call, read a blob as an *input binding*, and drop a
Service Bus / Queue message as an *output binding*, with zero client-construction code. Full
trigger/binding catalogue: `docs/FUNCTION_APPS.md`.

## `local.settings.json` — local dev config only

Local settings live in `src/Functions/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated"
  }
}
```

Key points (and interview traps):
- It supplies **local-only** configuration + connection strings when you run `func start` / F5.
  The `Values` entries become environment variables for the local host.
- **It is NOT published to Azure.** It's in `.gitignore` by convention and ignored on deploy —
  in the cloud these values come from the Function App's **Application settings** instead. So
  after publishing you must re-create every needed setting in the portal (or via IaC).
- **`AzureWebJobsStorage`** — every Function App needs a storage account for its own bookkeeping
  (triggers' state, timers, leases, logs). Here it points at **Azurite** (`UseDevelopmentStorage=true`)
  for local dev.
- **`FUNCTIONS_WORKER_RUNTIME: dotnet-isolated`** — this project uses the **.NET isolated worker
  model** (your code runs in its own process, decoupled from the host runtime version).

## Create a Function App and publish it

**Create (portal):** Create a resource → **Function App** → pick runtime stack (.NET 8 isolated),
region, **hosting plan** (Consumption for cheap/bursty), and it provisions a **storage account**
(the `AzureWebJobsStorage` backing) and optionally links **Application Insights**.

**Publish — three common ways:**
1. **Visual Studio / VS Code** → right-click the project → **Publish** → target the Function App
   (zip deploy). Easiest for this repo.
2. **Azure Functions Core Tools (CLI):**
   ```bash
   func azure functionapp publish <your-function-app-name>
   ```
3. **CI/CD** — GitHub Actions / Azure DevOps pipeline (zip deploy or `az functionapp deployment`).

**After publishing:** re-add any config that lived in `local.settings.json` under the Function
App's **Settings → Environment variables / Application settings** (e.g. `ServiceBusConnection`,
`Storage:ConnectionString`), because local settings don't travel with the deploy.

## Test a function in the portal

Once deployed: **Function App → Functions → pick a function → Code + Test → Test/Run.**
- You can set the **HTTP method, query params, route params, headers, and body**, hit **Run**,
  and see the **HTTP response + status** right there.
- For non-HTTP triggers (Timer/Service Bus/Blob) the same **Test/Run** panel lets you fire the
  function manually with sample trigger input, without waiting for the real event.

## Check the logs

Several layers, increasing in power:
- **Log stream** (Function App → **Monitoring → Log stream**) — live tail of the host + your
  `ILogger` output as invocations happen. Great for "is my call arriving right now."
- **Invocations / Monitor** per function — a table of recent executions with success/failure and
  duration.
- **Application Insights** — the real tool: end-to-end traces, dependencies, exceptions, custom
  metrics, and **KQL** queries over `requests` / `traces` / `exceptions`. This repo links App
  Insights (see `docs/APPLICATION_INSIGHTS.md` and `docs/APP_INSIGHTS_FUNCTIONS_DEMO.md`). Your
  `_logger.LogInformation(...)` calls (as in `HttpTriggerFunction`) surface here.

## Get the function URL — the URL options & function keys

For an HTTP function in the portal: **the function → Get Function URL.** The dropdown offers
different URLs depending on the **authorization level** the function was declared with
(`[HttpTrigger(AuthorizationLevel.X, ...)]`):

| Auth level | Needs a key? | Which key / URL option |
|------------|--------------|------------------------|
| **Anonymous** | No | Plain URL, no `code=` needed (this repo's `greet/{name}` is Anonymous) |
| **Function** | Yes | A **function key** (per-function) *or* a **host key** — passed as `?code=<key>` or header `x-functions-key` |
| **Admin** | Yes | The **master/host key** — full control; guard it |

**Function keys** are the built-in, simple way to protect an HTTP function without full identity:
- **Function keys** — scoped to one function.
- **Host keys** — work across **all** functions in the Function App; `default` is one; the
  **`_master`** key is the most privileged (also enables admin endpoints).
- Managed under the function's **Function Keys** / the app's **App Keys** blade — you can create,
  view, renew (rotate), and revoke them.
- Pass as query string `?code=<key>` **or**, more securely, the header `x-functions-key: <key>`.

Interview nuance: function keys are a **shared-secret** convenience, not real identity. For
production auth prefer **Entra ID / App Service Authentication (Easy Auth)** or put the function
behind **API Management** — see `docs/MANAGED_IDENTITY_ENTRA_ID.md`.

## Triggers — HTTP trigger = "API as a function"

Every function has exactly **one trigger** = what invokes it + its input. The **HTTP trigger**
is the "expose this code as an API endpoint" trigger — request in, response out, at a URL. The
other triggers (Timer, Service Bus, Queue, Blob, Event Grid, Event Hub, Cosmos change feed,
Durable) are covered with a full catalogue, bindings, and the Event-Grid-vs-polling-blob nuance
in `docs/FUNCTION_APPS.md`.

---

## Interview Questions

**Q: Difference between a Function and a Function App?**
A Function App is the deployable hosting container (the resource you create/publish/scale, holding
shared app settings, plan, storage, App Insights); a Function is a single trigger-driven method
inside it. Many functions live in one Function App.

**Q: Is one function equal to one API endpoint?**
For **HTTP-triggered** functions, yes — each is reached at its own URL like a Web API endpoint.
But Timer/Service Bus/Blob-triggered functions have no URL at all; they're woken by a
schedule/message/event, so the "one endpoint" idea only applies to HTTP triggers.

**Q: What is `local.settings.json` and does it deploy to Azure?**
It's local-only dev config (connection strings + settings, e.g. `AzureWebJobsStorage`,
`FUNCTIONS_WORKER_RUNTIME`) that becomes env vars for the local host. It is **not** published —
in Azure those values must be set as the Function App's Application settings.

**Q: How do you secure an HTTP-triggered function without full identity?**
Set its `AuthorizationLevel` to `Function` (or `Admin`) and require a **function/host key**,
passed as `?code=<key>` or the `x-functions-key` header. Rotate keys as needed. For real auth,
prefer Entra ID / Easy Auth or fronting with API Management — keys are a shared secret, not identity.

**Q: What's the difference between a function key, a host key, and the master key?**
A function key is scoped to a single function; a host key works across all functions in the app;
the `_master` key is the most privileged (also unlocks admin endpoints). Guard the master key.

**Q: Why are Functions cost-effective, and what does "scaling containers" mean here?**
On Consumption you pay per execution + run time with nothing charged while idle, and the platform
adds/removes instances (containers) automatically with load — even to zero. So bursty/event-driven
workloads don't pay for an always-on server.

**Q: How do you test a function and read its logs in the portal?**
Test via **Code + Test → Test/Run** (set method/params/body, or fire a sample event for non-HTTP
triggers). Logs via **Log stream** (live tail), the per-function **Monitor/invocations** table,
and **Application Insights** (traces/exceptions/KQL) for the full picture.

**Q: How do you publish this repo's Function App?**
Visual Studio/VS Code **Publish**, or `func azure functionapp publish <app-name>`, or a CI/CD
pipeline — then re-create the `local.settings.json` values as Application settings in the portal.
