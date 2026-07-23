# Azure Function Apps (HTTP, Timer, Event triggers)

> **Concepts here; operations next door.** This doc covers the *programming model* —
> triggers vs. bindings, plans, return-value semantics. For the *practical lifecycle* —
> create & publish a Function App, test in the portal, function URLs & keys, reading logs,
> and `local.settings.json` — see `docs/FUNCTION_APPS_PORTAL_AND_DEPLOY.md`.

## The plain-English version

A Function App is **code that runs only when something specific happens, and doesn't exist as a running process the rest of the time.** Contrast with `WebApp` in this repo: that's a Kestrel process listening on a port 24/7, whether or not anyone's calling it. A Function has no process sitting idle — the Azure Functions *host* is what's always running, and it invokes your method when its trigger condition fires, then your method returns and goes back to not running.

"Trigger" is the one word to remember here — **every Azure Function has exactly one trigger**, and the trigger is what decides *when* it runs and *what data it receives as input*. This repo has one of each of the three main trigger shapes:

- **HTTP trigger** (`HttpTriggerFunction.cs`) — runs when a URL is called. Functionally identical to a Controller action; the difference is entirely about hosting, not programming model.
- **Timer trigger** (`TimerTriggerFunction.cs`) — runs on a schedule (a cron-like expression), with nobody calling anything. "Run this cleanup job every night at 2am" is this shape, full stop.
- **Event trigger** (`OrderCreatedFunction.cs`, Service Bus-triggered) — runs when a message arrives somewhere else (a queue, in this case; could equally be a new Blob uploaded, or an Event Grid event). Nobody calls this function directly either — it's woken up by something happening in another Azure service.

## The analogy that makes it click

Think of three kinds of factory workers. The **HTTP-triggered** one sits at a service counter and only works when a customer walks up and asks for something — a normal request/response job. The **timer-triggered** one has an alarm clock and does a fixed task at 2am regardless of whether any customer ever shows up — nobody asked, the clock did. The **event-triggered** one has a walkie-talkie clipped to their belt; they do nothing until a message crackles through from a completely different department (in this repo: the WebApp publishing to Service Bus), and then they act on whatever's in that message. All three are "workers," but what wakes each of them up is completely different — that's the entire mental model for "trigger."

## Why this matters commercially (the interview-relevant part)

You pay for Function Apps differently than for an always-on App Service. On the **Consumption plan**, you pay per execution and per second of actual execution time — if your function never fires, you pay nothing for compute. That's the entire pitch for event-driven/scheduled workloads: a nightly cleanup job or a "process a message when one shows up" worker doesn't need (and shouldn't cost as much as) a server that's provisioned and billed 24/7 just to be idle 99% of the time.

## How this repo implements it

All three live in `src/Functions/`, one file each:

- **`HttpTriggerFunction.cs`** — `[HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "greet/{name}")]`. `AuthorizationLevel.Anonymous` means no function key required to call it (there's also `Function`, requiring a key, and `Admin`, requiring the master key) — verified locally: `curl http://localhost:7071/api/greet/Interviewer` returned `{"message":"Hello, Interviewer!", ...}`.
- **`TimerTriggerFunction.cs`** — `[TimerTrigger("0 */5 * * * *")]`, an NCRONTAB expression (6 fields — includes seconds, unlike standard 5-field cron) meaning "every 5 minutes." Verified locally by temporarily changing the schedule to every 10 seconds and confirming it fired three times in a row in the Functions host log, then reverting to the real 5-minute schedule before committing.
- **`OrderCreatedFunction.cs`** — `[ServiceBusTrigger("orders", Connection = "ServiceBusConnection")]`. This is the receiver half of the Service Bus publisher/receiver pair — see `docs/SERVICE_BUS.md`. Confirmed registering correctly with the Functions host, and confirmed failing to *start listening* without a real Service Bus connection string configured (expected — no local Service Bus emulator was available; this is documented as reference-only, not fully exercised end-to-end).

## The full trigger & binding catalogue

This repo ships HTTP, Timer, and Service Bus triggers, but Functions supports many
more — and the concept that ties them together is **triggers vs. bindings**:

- **Trigger** — *what invokes the function.* Exactly **one** per function, and it also
  provides the input data. (Every function must have exactly one trigger.)
- **Input binding** — *extra data pulled in* for you before the function runs (e.g.
  read a specific blob or a Cosmos document by id). Optional, zero or more.
- **Output binding** — *where results go* after the function runs (e.g. write a blob,
  send a queue/Service Bus message) without you constructing the client yourself.
  Optional, zero or more.

Bindings are **declarative** — you attach an attribute and the runtime handles the
client/connection plumbing. The connection info for any trigger/binding comes from an
**app setting** whose name you reference (like `Connection = "ServiceBusConnection"` in
this repo), not a hardcoded string.

### The common triggers

| Trigger | Fires when… | Typical use |
|---|---|---|
| **HTTP** | a URL is called | APIs, webhooks (in this repo: `HttpTriggerFunction`) |
| **Timer** | a schedule (NCRONTAB) elapses | nightly/periodic jobs (`TimerTriggerFunction`) |
| **Service Bus** | a message lands on a queue/topic subscription | decoupled processing (`OrderCreatedFunction`) |
| **Queue Storage** | a message lands on a **Storage Queue** | cheap/simple background work, fan-out (see `docs/AZURE_STORAGE_QUEUE.md`) |
| **Blob Storage** | a **blob is created/updated** in a container | "process a file when it's uploaded" (thumbnail, virus scan, import) |
| **Event Grid** | a discrete event is published (incl. Blob-created events) | reactive, low-latency event handling (preferred over Blob trigger at scale) |
| **Event Hub** | events arrive on a stream | high-throughput telemetry/streaming ingestion |
| **Cosmos DB** | documents change (change feed) | react to data changes, materialized views |
| **Durable** (orchestration/activity/entity) | orchestrator drives them | stateful workflows (see `docs/DURABLE_FUNCTIONS.md`) |

### The Blob trigger, and the Event Grid caveat worth knowing

A **Blob trigger** runs your function when a blob appears/changes:
```csharp
[Function("ThumbnailMaker")]
public async Task Run(
    [BlobTrigger("uploads/{name}", Connection = "StorageConnection")] Stream blob,
    string name) { /* ... */ }
```
The interview nuance: the **classic (polling) Blob trigger can be slow/unreliable at
scale** — it scans the container for changes and can miss or lag on high volume. The
recommended modern approaches are the **Event Grid-based blob trigger** (source =
`EventGrid`) or an **Event Grid trigger** subscribed to Blob "created" events — both
are push-based and near-instant. Knowing "prefer Event Grid over the polling blob
trigger for scale/latency" is a common AZ-204 point.

### Input & output bindings (example)

One function can trigger on one thing and bind to others — e.g. triggered by an HTTP
call, read a blob as input, and write a queue message as output, all declaratively:
```csharp
[Function("Import")]
[QueueOutput("processed", Connection = "StorageConnection")]   // return value -> queue message
public string Run(
    [HttpTrigger("post")] HttpRequestData req,
    [BlobInput("config/settings.json", Connection = "StorageConnection")] string settings)
{
    // 'settings' was fetched from Blob Storage for you; the returned string is
    // enqueued to the 'processed' Storage Queue for you.
    return "done";
}
```
This is the payoff of bindings: no `BlobClient`/`QueueClient` construction, no
connection wiring in code — just attributes.

### Auto-complete on message triggers (recap)

For the Service Bus / Queue triggers, the runtime **auto-completes** the message when
your function returns successfully and abandons it on an exception (so it retries and
eventually dead-letters). To settle manually, disable auto-complete and use the
message-actions parameter — see the Functions-bindings note in `docs/SERVICE_BUS.md`.

## What "return value" means differs by trigger

The HTTP trigger returns an `IActionResult` — that becomes the actual HTTP response, same idea as a Controller. The Timer trigger returns `void` — there's no caller waiting for a response, so there's nothing to return *to*. The Service Bus trigger also effectively returns nothing meaningful to the caller (there is no caller) — instead, whether the method **throws or returns normally** is what matters: returning normally completes (removes) the message from the queue; throwing leaves it for the queue's automatic retry, and eventually the dead-letter subqueue.

---

## Interview Questions

**Q: What is a "trigger" and why is it the central concept in Azure Functions?**
It's what starts the function running and what shape of data it receives. Every Function has exactly one trigger (though it can have multiple additional *bindings* for output). Understanding a Function App means understanding its trigger — "how does this get invoked, and what happens if that thing doesn't happen."

**Q: What's the difference between an HTTP-triggered Function and a Controller action in a Web API?**
Almost nothing at the programming-model level — both take a request, do work, return a response. The difference is hosting and billing: a Function on a Consumption plan has no dedicated always-on process and is billed per-execution; a Controller lives inside an always-running App Service process. You'd pick a Function for infrequent, bursty, or unpredictable HTTP traffic where paying for idle capacity doesn't make sense, and a Controller/App Service for steady, latency-sensitive traffic where cold starts are a problem.

**Q: What is NCRONTAB, and how does the timer schedule in this repo work?**
It's cron with an extra leading field for seconds — 6 fields instead of the usual 5: `{second} {minute} {hour} {day} {month} {day-of-week}`. `"0 */5 * * * *"` means "at second 0, every 5th minute" — i.e., every 5 minutes. This was verified locally by temporarily setting it to `"*/10 * * * * *"` (every 10 seconds) and watching it fire three times in the Functions host log before reverting.

**Q: Why does the Timer function return void while the HTTP function returns IActionResult?**
Because there's no caller waiting for a response on a schedule-fired trigger — there's nobody to send a result back to. The HTTP trigger has an actual caller on the other end of the request, so its return value becomes the HTTP response body/status.

**Q: What happens if a Service Bus-triggered Function throws an exception?**
The message is NOT completed/removed from the queue. Service Bus's own retry policy (`MaxDeliveryCount`) redelivers it, and if it keeps failing past that count, the message moves to the queue's dead-letter subqueue instead of being retried forever or silently lost. Returning normally (no exception) is what tells the Functions runtime to complete the message.

**Q: What's the Consumption plan and why does it matter for Function Apps specifically?**
It's a billing model where you pay per execution + execution duration, with no charge for idle time, and the platform automatically scales the number of running instances (including to zero) based on load. It's the pricing argument for choosing a Function over an always-on App Service for anything that doesn't run constantly — a nightly timer job or a queue processor that's idle most of the day.

**Q: What's the difference between a trigger and a binding?**
A trigger is what invokes the function and supplies its input — exactly one per
function. Bindings are declarative connections to other services: input bindings pull
extra data in before the function runs, output bindings send results out after — zero
or more of each, optional. Both remove client/connection plumbing from your code; you
attach an attribute and the runtime handles it.

**Q: Name several Azure Functions triggers beyond HTTP/Timer.**
Service Bus (queue/topic message), Queue Storage (Storage Queue message), Blob Storage
(blob created/updated), Event Grid (discrete events, incl. blob-created), Event Hub
(streaming/telemetry), Cosmos DB (change feed), and the Durable
orchestration/activity/entity triggers.

**Q: Why prefer an Event Grid-based blob trigger over the classic Blob trigger?**
The classic Blob trigger polls the container for changes, which can lag or miss events
at high volume. The Event Grid-based blob trigger (or an Event Grid trigger subscribed
to Blob "created" events) is push-based and near-instant, so it scales and reacts far
better — the recommended approach for production blob-driven processing.

**Q: This repo's OrderCreatedFunction failed to start locally — is that a bug?**
No — it's `[ServiceBusTrigger("orders", Connection = "ServiceBusConnection")]` failing because no real Service Bus namespace/connection string was configured, which is expected in this environment (no free local Service Bus emulator was available, unlike Blob Storage's Azurite). The HTTP and Timer triggers in the same host started and ran correctly in the same process — one trigger failing to bind doesn't take down the others.
