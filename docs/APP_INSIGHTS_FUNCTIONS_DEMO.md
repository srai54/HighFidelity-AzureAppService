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

## Exceptions and errors in App Insights

Two more functions in `AppInsightsDemoFunctions.cs` demonstrate how failures
surface — because "does it capture exceptions" is the single most common
follow-up after "does it capture requests":

```csharp
[Function("Function3Unhandled")]   // GET /api/function-3-unhandled
// throws InvalidOperationException and does NOT catch it

[Function("Function4Handled")]     // GET /api/function-4-handled
// catches its own exception, reports it via _logger.LogError(ex, ...),
// and returns a controlled 500
```

### The two kinds, and how each looks in the portal

| | Function3 (unhandled) | Function4 (handled) |
|---|---|---|
| What happens | Exception escapes the function | Exception caught inside a `try/catch` |
| Caller sees | 500 (uncontrolled) | 500 (controlled JSON error body) |
| Captured by | **Automatic** — the runtime records it | `_logger.LogError(ex, ...)` you wrote |
| App Insights telemetry | A **failed request** + an **exception** item (stack trace) | An **exception/error trace** attached to the (successful-or-500) request |
| Correlation | Both correlated to the request's operation id | Correlated to the request's operation id |

The key theory point: **an unhandled exception is captured for free** — you write
no telemetry code, the exception escaping the invocation is enough. A **handled**
exception is invisible to App Insights *unless you report it* — because you
swallowed it, the platform never sees it, so `_logger.LogError(ex, ...)` (or, in
the classic-SDK WebApp, `TelemetryClient.TrackException(ex)`) is what puts it in
front of you. This is the trade-off to state in an interview: catching an
exception to keep the app running also *hides* it from monitoring unless you
explicitly log it.

### Where to see them in the Azure Portal

Open the App Insights resource → **Failures** (left menu):
- The **Operations** tab shows failed *requests* — `function-3-unhandled` will
  appear here with a failed-request count and its 500s.
- The **Exceptions** tab shows exception *types* — `InvalidOperationException`
  (from Function3) and `TimeoutException` (from Function4), each with a count and
  drill-in to the full stack trace and the request that threw it.
- Click any exception → the **end-to-end transaction** view shows the request, the
  exception, and the `Function3 invoked...` / `Function4 caught...` trace lines all
  under one operation id — the whole story of that one failed call.

### Querying failures directly (Logs / KQL)

Under **Logs**, the `exceptions` and `requests` tables are queryable with KQL:
```kusto
// every exception in the last hour, newest first
exceptions
| where timestamp > ago(1h)
| project timestamp, type, outerMessage, operation_Name
| order by timestamp desc

// failed requests, and how many there were
requests
| where timestamp > ago(1h) and success == false
| summarize failures = count() by name, resultCode
```
`operation_Name` / `operation_Id` are the join keys that tie an exception back to
its request — the same correlation idea from `docs/APPLICATION_INSIGHTS.md`.

### Generating the failures to look at

```bash
curl -i https://func-highfid-$SUFFIX.azurewebsites.net/api/function-3-unhandled   # -> 500, unhandled
curl -i https://func-highfid-$SUFFIX.azurewebsites.net/api/function-4-handled      # -> 500, handled+logged
```
Then watch them appear in **Failures** (1–3 min lag), or in **Live Metrics** where
the failure rate ticks up immediately.

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

**Q: Are exceptions captured automatically, or do you have to write code?**
Unhandled exceptions — the ones that escape your function/controller — are captured automatically: the runtime records both a failed request and an exception telemetry item with the stack trace, no code required. Handled exceptions (ones you catch) are NOT captured automatically, because you swallowed them before the platform could see them — you have to report them yourself via `_logger.LogError(ex, ...)` or `TelemetryClient.TrackException(ex)`. The catch to name in interviews: catching to keep the app alive also hides the failure from monitoring unless you explicitly log it.

**Q: Where do you look in the portal for errors, and how do you tie an exception to the request that caused it?**
The Failures blade — its Operations tab lists failed requests, its Exceptions tab lists exception types with counts and stack traces. Clicking through gives the end-to-end transaction view, which shows the request, the exception, and the correlated log traces under one operation id. In Logs/KQL, the `exceptions` and `requests` tables share `operation_Id`/`operation_Name`, which is how you join an exception back to its request.

**Q: Isolated worker vs. in-process model — does App Insights integration differ?**
Yes. In-process functions share the host process, so the classic App Insights SDK integrates directly. Isolated-worker functions (this repo, .NET 8) run in a separate process, so worker telemetry is forwarded either via OpenTelemetry + Azure Monitor exporter (used here, the recommended path) or the `Microsoft.Azure.Functions.Worker.ApplicationInsights` package. The host-level request telemetry works the same either way — it's the worker's own telemetry pipe that differs.
