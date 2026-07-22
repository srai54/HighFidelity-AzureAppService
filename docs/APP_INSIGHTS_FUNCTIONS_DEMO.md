# Application Insights for a Function App — Simple End-to-End Demo

This is the hands-on companion to `docs/APPLICATION_INSIGHTS.md` (telemetry
theory) and `docs/FUNCTION_APPS.md` (Functions theory): deploy a Function App
with two trivial functions, wire it to Application Insights, call the functions,
and watch them appear in **Live Metrics** and **Performance** in the Azure Portal.

## The example

Two HTTP functions, deliberately as simple as possible so the focus stays on the
monitoring, not the logic — `src/Functions/AppInsightsDemoFunctions.cs`:

```csharp
[Function("Function1")]
public IActionResult Function1(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "function-1")] HttpRequest req)
{
    _logger.LogInformation("Function1 invoked at {Utc}", DateTime.UtcNow);
    return new OkObjectResult("function-1");   // GET /api/function-1 -> "function-1"
}

[Function("Function2")]
public IActionResult Function2(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "function-2")] HttpRequest req)
{
    _logger.LogInformation("Function2 invoked at {Utc}", DateTime.UtcNow);
    return new OkObjectResult("function-2");   // GET /api/function-2 -> "function-2"
}
```

The `_logger.LogInformation(...)` lines aren't decoration — once the Function App
is linked to Application Insights, those become **trace** telemetry, automatically
correlated to the request that produced them. You didn't write any App-Insights-
specific code to get that; it comes from the host being connected to the resource.

## Theory — how App Insights attaches to a Function App (vs. a Web App)

For an **App Service / Web App**, App Insights is an in-process SDK you register
(`AddApplicationInsightsTelemetry()` — see `docs/APPLICATION_INSIGHTS.md`).

For a **Function App**, it's wired at two levels, and understanding the split is
the common interview point:

1. **The Functions host itself** (the Azure-managed runtime hosting your code) is
   connected to Application Insights purely by an app setting —
   `APPLICATIONINSIGHTS_CONNECTION_STRING`. Set that, and the *platform* reports
   every function invocation, its duration, success/failure, and basic request
   telemetry with **zero code** in your project. This is why the Function App
   shows up in App Insights even for functions that do nothing but return a string.

2. **The isolated worker process** (your .NET 8 code, running in a separate
   process from the host) needs to opt in to forward *its* telemetry — its
   `ILogger` traces, custom telemetry, and richer dependency tracking. This repo
   does that in `src/Functions/Program.cs` via OpenTelemetry:
   ```csharp
   if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
   {
       builder.Services.AddOpenTelemetry()
           .UseFunctionsWorkerDefaults()
           .UseAzureMonitorExporter();
   }
   ```
   Same connection-string switch, so it only turns on when the app setting is
   present (locally, absent = it stays off and the app still runs). OpenTelemetry
   + `Azure.Monitor.OpenTelemetry.Exporter` is Microsoft's current recommended
   path for isolated-worker Functions telemetry — the classic App Insights SDK
   is in maintenance mode (see `docs/APPLICATION_INSIGHTS.md`).
   > Alternative you may see: the `Microsoft.Azure.Functions.Worker.ApplicationInsights`
   > package + `.ConfigureFunctionsApplicationInsights()`. That's the classic-SDK
   > equivalent for isolated worker; this repo uses the OpenTelemetry route instead.

The takeaway: **the platform-level request telemetry is free (app setting only);
the worker's own traces/custom telemetry need the Program.cs opt-in.**

## Steps to deploy the Function App and configure App Insights

Full commands are in `scripts/04-functions.azcli` and `scripts/02-app-insights.azcli`
— this is the narrative version. Assumes `az login` and the shared variables from
`scripts/00-resource-group.azcli` (`$RG`, `$LOCATION`, `$SUFFIX`).

**1. Create the App Insights resource** (`scripts/02`) — a Log Analytics workspace
+ an App Insights component. Grab its connection string.

**2. Create the Function App, linked to App Insights in one step** (`scripts/04`):
```bash
az functionapp create \
  --name "func-highfid-$SUFFIX" \
  --resource-group "$RG" \
  --consumption-plan-location "$LOCATION" \
  --runtime dotnet-isolated --runtime-version 8 --functions-version 4 \
  --storage-account "stfunchighfid$SUFFIX" \
  --app-insights "appi-highfid-$SUFFIX"      # <-- this links it and sets the connection string app setting for you
```
The `--app-insights` flag makes Azure set `APPLICATIONINSIGHTS_CONNECTION_STRING`
on the Function App automatically — which is exactly the switch both the host and
the Program.cs OpenTelemetry block key off of. (If you created the Function App
without it, set it manually:
`az functionapp config appsettings set --name ... --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=<conn>"`.)

**3. Deploy the code** (from `src/Functions`):
```bash
func azure functionapp publish "func-highfid-$SUFFIX"
```

**4. Call the functions** to generate telemetry:
```bash
curl https://func-highfid-$SUFFIX.azurewebsites.net/api/function-1   # -> function-1
curl https://func-highfid-$SUFFIX.azurewebsites.net/api/function-2   # -> function-2
# hit them a few times so there's something to see:
for i in $(seq 1 20); do curl -s https://func-highfid-$SUFFIX.azurewebsites.net/api/function-1 > /dev/null; done
```

## Checking Live Metrics and Performance

In the Azure Portal, open the **Application Insights resource** (`appi-highfid-<suffix>`),
not the Function App:

- **Live Metrics** (left menu → *Live Metrics*) — a real-time stream: incoming
  request rate, duration, failure rate, and server health, updating per-second
  **while you call the functions**. Run the `for` loop above and watch the request
  rate spike live. This is the fastest "is my telemetry actually flowing?" check —
  it has near-zero delay, unlike the other views which lag a couple minutes.
- **Performance** (left menu → *Performance*) — aggregated timings per operation.
  You'll see `function-1` and `function-2` listed as separate operations, each
  with its request count and duration percentiles (50th/95th/99th). This is where
  you'd spot "function-2 is slower than function-1" or a p99 latency problem.
- **Transaction search** (or *Logs*) — drill into an individual invocation and see
  the request plus the `Function1 invoked at ...` trace line correlated under the
  same operation id.
- **Application Map** — with the WebApp also deployed and reporting, this draws the
  services as connected nodes (this is where the cloud-role-name initializer in
  `docs/APPLICATION_INSIGHTS.md` matters, so each service is a distinct box).

Expect a **1–3 minute lag** for everything except Live Metrics. If nothing shows
up after a few minutes: confirm `APPLICATIONINSIGHTS_CONNECTION_STRING` is actually
set on the Function App (`az functionapp config appsettings list ...`), and that you
called the functions *after* the setting was applied + the app restarted.

## What's real vs. reference-only in this repo

The two functions **compile and build cleanly** (0 warnings), and the App Insights
wiring in `Program.cs` matches the real isolated-worker OpenTelemetry approach. As
documented in `docs/ARCHITECTURE.md`, they could not be **run** locally this session
(the machine's Smart App Control policy blocks the Functions host from loading its
freshly-built worker DLL), and there's no Azure subscription here to deploy to — so
the Live Metrics / Performance walkthrough above is the correct procedure, written
from how the tooling works, not a captured screenshot from this environment. On an
unrestricted machine with a real Azure account, the steps run exactly as written.

---

## Interview Questions

**Q: How does a Function App get connected to Application Insights?**
Primarily by one app setting — `APPLICATIONINSIGHTS_CONNECTION_STRING`. Set it (the portal does this automatically when you link an App Insights resource, or the `az functionapp create --app-insights` flag does), and the Functions host reports invocation count, duration, and success/failure for every function with no code. Creating the Function App with `--app-insights` wires this in one step.

**Q: If the platform reports invocations automatically, why is there any telemetry code in Program.cs at all?**
Because the host-level telemetry only covers the platform's view (the request happened, how long it took). Your isolated worker process's own signals — `ILogger` traces, custom events/metrics, richer dependency tracking — live in a separate process and need to be forwarded explicitly. That's the `AddOpenTelemetry().UseFunctionsWorkerDefaults().UseAzureMonitorExporter()` block, gated on the same connection-string setting.

**Q: What's the fastest way to confirm telemetry is flowing after a deploy?**
Live Metrics — it's a near-real-time stream (sub-second), so you call the function and immediately watch the request rate move. Every other view (Performance, Transaction search, Logs) lags 1–3 minutes because it goes through ingestion + indexing first.

**Q: You deployed and called the function but see nothing in Performance — what do you check?**
First, that `APPLICATIONINSIGHTS_CONNECTION_STRING` is actually set on the Function App and points at the right resource. Second, that you called the function *after* the setting was applied and the app restarted. Third, whether you're just inside the ingestion lag (check Live Metrics, which is immediate, to rule that out). Fourth, that you're looking at the App Insights resource, not the Function App blade.

**Q: Isolated worker vs. in-process model — does App Insights integration differ?**
Yes. In-process functions share the host process, so the classic App Insights SDK integrates directly. Isolated-worker functions (this repo, .NET 8) run in a separate process, so worker telemetry is forwarded either via OpenTelemetry + Azure Monitor exporter (used here, the recommended path) or the `Microsoft.Azure.Functions.Worker.ApplicationInsights` package. The host-level request telemetry works the same either way — it's the worker's own telemetry pipe that differs.
