# Durable Functions — Stateful Orchestration on Top of Functions

> A Durable Functions orchestrator is also the natural way to implement an
> **orchestration-style Saga** (distributed transaction across services with
> compensations) — see `docs/SAGA_PATTERN.md`.

## The plain-English version

A plain Azure Function is stateless and short-lived: it wakes up, does one thing, and dies. That's fine until you need a *workflow* — several steps that have to happen in order, or in parallel-then-rejoin, possibly spanning minutes, hours, or days, where you need to remember exactly where you were if the process restarts halfway through.

**Durable Functions** is an extension that adds a stateful workflow engine on top of the regular Functions runtime. The trick that makes it work: an **orchestrator function** doesn't run start-to-finish like normal code. Every time it hits an `await`, execution stops, and Durable Task saves what happened so far (an event history) to storage. When it needs to continue (an activity finished, a timer fired), it **replays** the orchestrator function from the very top — but this time, every already-completed step reads its result instantly from history instead of actually re-running, so execution fast-forwards straight to the new work. This is why orchestrator code has a hard rule: **it must be deterministic**. No `DateTime.Now`, no `Guid.NewGuid()`, no direct database/HTTP calls inside the orchestrator itself — anything like that has to happen inside an **activity function**, which is allowed to do normal, non-deterministic I/O, because activities aren't replayed the same way.

Three roles, every Durable Functions app:
- **Client function** — a normal trigger (HTTP, queue, whatever) that starts an orchestration and hands back a way to check on it.
- **Orchestrator function** — describes the workflow: what runs, in what order, in parallel or not. Pure, replay-safe logic only.
- **Activity function(s)** — the actual work. Can call databases, APIs, do anything a normal function can do.

## The analogy that makes it click

Think of the orchestrator as a **recipe**, not a cook. The recipe says "boil water, then simultaneously chop the onions and marinate the meat, then combine once both are done" — it never touches a knife itself. The activity functions are the actual cooks executing each step. If the kitchen loses power mid-recipe, you don't start over from scratch — you look at the recipe card, see which steps have a checkmark already, and resume from there. That checkmark list is the event history; re-reading the recipe from the top to find your place is the "replay."

## How this repo implements it

**`src/Functions/OrderBatchOrchestration.cs`** implements a fan-out/fan-in workflow: validate a batch of orders in parallel, then run one final "summarize" step once they're all done — chaining and fan-out/fan-in in the same example, both extremely common real interview-question shapes.

```csharp
[Function(nameof(StartOrderBatch))]                 // 1. Client — HTTP trigger, POST /api/orders/batch
[Function(nameof(OrderBatchOrchestrator))]          // 2. Orchestrator — fan-out to N activities, fan-in, then chain to a final activity
[Function(nameof(ValidateOrderActivity))]           // 3a. Activity — the parallel work
[Function(nameof(FinalizeBatchActivity))]           // 3b. Activity — the final chained step
```

The orchestrator:
```csharp
var validationTasks = orderIds.Select(id => context.CallActivityAsync<bool>(nameof(ValidateOrderActivity), id)).ToList();
var validationResults = await Task.WhenAll(validationTasks);   // fan-out/fan-in
var summary = await context.CallActivityAsync<string>(nameof(FinalizeBatchActivity), new BatchResult(...));  // chained final step
```

Calling the client endpoint doesn't return the batch result directly — it returns `202 Accepted` with status-check URLs (`client.CreateCheckStatusResponseAsync`), which is the standard **async HTTP API / polling pattern**: the caller polls a status URL until the orchestration reports `Completed`, rather than holding a connection open for however long the whole workflow takes.

## What's real vs. reference-only in this repo

The code compiles cleanly against the real `Microsoft.Azure.Functions.Worker.Extensions.DurableTask` package and matches the actual isolated-worker Durable Functions API (`DurableTaskClient`, `TaskOrchestrationContext`, `[OrchestrationTrigger]`, `[ActivityTrigger]`). **It could not be run in this environment** — the same Windows Smart App Control restriction documented in `docs/RESILIENCE_POLLY.md` and `docs/ARCHITECTURE.md` blocks the Functions host from loading its own freshly-built worker DLL (confirmed via the same CodeIntegrity event log signature, this time against `Functions.dll`). Durable Functions also needs the same Azure Storage backing (Azurite locally) that the HTTP/Timer triggers already use for their bindings — that part of the setup is unaffected, only the process launch itself is blocked. If this restriction is lifted, `func start` (with Azurite running) plus `curl -X POST http://localhost:7071/api/orders/batch`, then polling the returned status URL, is exactly how to verify it.

---

## Interview Questions

**Q: Why must an orchestrator function be deterministic — what actually breaks if it isn't?**
Because the orchestrator is *replayed* from the start every time it wakes up after an `await`, using its saved event history to skip straight past already-completed work. If the orchestrator called `DateTime.Now` or `Guid.NewGuid()` directly, the value would be different on each replay, and the orchestrator's decisions (which branch to take, what input to pass an activity) could diverge from what actually happened the first time — corrupting the workflow. Anything non-deterministic has to be pushed into an activity, or use the Durable Task-provided equivalents (e.g. `context.CurrentUtcDateTime` instead of `DateTime.Now`).

**Q: What's the difference between an orchestrator function and an activity function?**
The orchestrator describes *what should happen and in what order* — it's replayed repeatedly and must be deterministic, no direct I/O. The activity function is where the actual work happens — a normal function, allowed to call databases/APIs/anything, and is *not* replayed the same way (once it completes, its result is just a fact recorded in history).

**Q: What's "fan-out/fan-in" and when would you use it?**
Fan-out: kick off many activity calls in parallel (e.g., validate 100 orders at once instead of one at a time). Fan-in: wait for all of them to finish before continuing (`Task.WhenAll` inside the orchestrator). Use it whenever a batch of independent work items can run concurrently but you need to know they're *all* done before the next step — exactly this repo's order-batch-validation-then-summarize example.

**Q: How does a client know when a long-running orchestration has finished?**
The client function returns a status-check response (`CreateCheckStatusResponseAsync`) containing a URL the Durable Task extension manages itself. The caller polls that URL, which returns the orchestration's current status (`Running`, `Completed`, `Failed`) and, once complete, its output. This is the standard async-HTTP-API pattern — you don't hold an HTTP connection open for a workflow that might take minutes or hours.

**Q: What happens if the Functions host restarts in the middle of a long-running orchestration?**
Nothing is lost. Every awaited step's completion is durably recorded (backed by Azure Storage — queues, tables, blobs) as it happens. When the host comes back, the Durable Task framework rehydrates the orchestration by replaying its function from the top against that saved history, fast-forwarding through everything already completed, and resumes exactly where it left off.

**Q: Beyond fan-out/fan-in, what other Durable Functions patterns come up in interviews?**
Function chaining (step 2 only starts after step 1's output, no parallelism — trivial with `await` in sequence), the async HTTP API pattern (used here for the client's status-check response), human interaction/approval workflows (an orchestrator that waits on an external event with a timeout, e.g. "wait up to 24h for manager approval"), and monitoring (an orchestrator that polls some external condition on a timer indefinitely, e.g. "check this resource's status every hour until it's ready").
