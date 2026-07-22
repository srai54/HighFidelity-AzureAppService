# Azure Service Bus — Queue (Publisher + Receiver)

## What is it, in one paragraph

**Azure Service Bus is a fully-managed message broker** — a middleman that reliably holds messages between the code that *produces* work and the code that *consumes* it. A producer drops a message onto a **queue** (or a **topic**); Service Bus stores it durably until a consumer picks it up and confirms it's done. Because the broker sits in the middle, the producer and consumer never call each other directly — and that one fact is what buys you the two properties this doc is really about: **decoupling** (the two sides don't depend on each other being up, fast, or even deployed together) and **load balancing** (many consumers can share the work of draining one queue). It's Azure's "enterprise" messaging option, richer than the simpler Storage Queues (ordering via sessions, transactions, dead-lettering, topics/subscriptions).

## The plain-English version

A queue solves one problem: **the thing that creates work and the thing that does the work don't need to run at the same time, or even both be up at the same moment.** Without a queue, `OrdersController` would have to call the order-processing logic directly, in-process, and if that processing is slow (charge a card, call a shipping API, write to three tables) the customer's HTTP request sits there waiting for all of it. With a queue, the controller does one fast thing — "drop a message describing what happened" — and returns immediately. Something else, whenever it's ready, picks that message up and does the slow work.

Service Bus specifically (as opposed to Storage Queues, the cheaper/simpler alternative) adds things a basic queue doesn't have: guaranteed FIFO ordering (with sessions), transactions, a dead-letter subqueue for poison messages, and topics/subscriptions for one message to fan out to multiple independent receivers.

## The analogy that makes it click

Think of a restaurant kitchen ticket rail. The waiter (the **publisher** — `OrdersController` in this repo) writes an order on a ticket and clips it to the rail, then immediately goes back to serving other tables — they don't stand at the pass watching it cook. The chef (the **receiver** — `OrderCreatedFunction` in this repo) works through tickets on the rail whenever they're free, in whatever order they come. Crucially: **the waiter never talks to the chef directly.** If the chef is on a break, tickets just wait on the rail — nothing is lost, and the waiter's job (taking orders) is completely unaffected by whether the kitchen is fast or slow right now. That decoupling is the entire value of a queue.

## The building blocks — Namespace, Queue, Topic, Subscription

Four entities, in a hierarchy. Getting these straight is foundational (and a
very common interview opener):

- **Namespace** — the top-level container and the network endpoint (e.g.
  `sb-highfid-1234.servicebus.windows.net`). It's like a "server" for messaging:
  the connection string points at a namespace, and queues/topics live *inside*
  it. You pick a pricing tier at the namespace level, and that tier decides
  what's available inside (see the three tiers below).
- **Queue** — a single line of messages with **one logical consumer side**:
  one message goes to one consumer (spread across competing-consumer instances,
  as above). This is **point-to-point**: producer → queue → (a) consumer. This
  repo uses a queue (`orders`).
- **Topic** — looks like a queue to the *sender* (you publish a message to it the
  same way), but it's built for **one-to-many** fan-out. A topic on its own
  doesn't hold consumers; it forwards each message to all of its subscriptions.
- **Subscription** — a named "virtual queue" attached to a topic. Each
  subscription gets **its own independent copy** of every message published to the
  topic (subject to its filter, if any), and each is consumed independently — with
  its own competing consumers, its own dead-letter queue, etc.

**Queue vs. Topic+Subscription — the one-line distinction:** a *queue* is
**point-to-point** — one sending side and one logical receiving side, each message
handled by a single consumer (work distribution). A *topic* is **one-to-many** —
you attach a **separate subscription per interested receiver**, and every
subscription gets its own copy of each message, consumed by its own receiver
(publish/subscribe, fan-out). Put simply: on a queue a message is consumed once;
on a topic the same message is delivered to every subscription, so N subscriptions
= N independent receivers each getting their own copy.
Use a queue when one logical worker should handle each message; use a topic when
several *different* services each independently need to react to the same event.

### The three pricing tiers (set at the namespace level)

| Tier | Queues | Topics/Subscriptions | Notable | Typical use |
|---|---|---|---|---|
| **Basic** | ✅ | ❌ (queues only) | Cheapest; no topics, no sessions, small message size | Simple "just a queue" background work |
| **Standard** | ✅ | ✅ | Topics, sessions, transactions, dead-lettering; pay-per-operation | Most apps — the common default |
| **Premium** | ✅ | ✅ | Dedicated capacity (predictable throughput/latency), larger messages, VNet/private-endpoint isolation, geo-DR | Production at scale, isolation/compliance needs |

The single fact worth memorizing: **topics require Standard or Premium — Basic
only allows plain queues.** So if an interview scenario needs fan-out (topics),
Basic is immediately ruled out. This repo's `scripts/05-service-bus.azcli`
creates a **Basic** namespace because it only uses one queue; add a topic and
you'd bump it to Standard.

Concrete example: if "order placed" should be handled once (process the order),
that's a **queue**. If "order placed" should simultaneously (a) trigger
fulfillment, (b) update the analytics warehouse, and (c) send a confirmation
email — three different services each needing their own copy — that's a **topic**
with three **subscriptions**, one per service. Subscriptions can also carry
**filters** (SQL-like rules) so a subscription only receives the subset of
messages it cares about (e.g. `amount > 1000` → a "large orders" subscription).

This maps onto the messaging-comparison doc too: a topic's fan-out overlaps
conceptually with Event Grid, but Service Bus topics are pull-based, ordered, and
durable per-subscription — see `docs/MESSAGING_COMPARISON.md`.

## Decoupling services — the first big reason

"Decoupling" means the producer and consumer depend on **the queue**, not on **each other**. Concretely, that removes four kinds of dependency that a direct in-process (or direct HTTP) call forces on you:

- **Temporal decoupling (time):** they don't have to be up at the same instant. If `OrderCreatedFunction` is redeploying or crashed, `OrdersController` keeps accepting orders — messages queue up and get processed when the consumer returns. A direct call would fail the moment the downstream is down.
- **Performance decoupling (speed):** the producer returns as soon as the message is enqueued (fast), regardless of how slow the actual processing is. The customer's HTTP request isn't held hostage to a card charge + shipping API + three DB writes.
- **Deployment decoupling (lifecycle):** WebApp and Functions are separate Azure resources, released on their own schedules. Neither redeploy forces the other. This is exactly why `OrderCreatedMessage` is *redefined* on each side rather than shared via a project reference — sharing the type would recouple their build/deploy lifecycles; the JSON on the wire is the real contract.
- **Failure decoupling (blast radius):** a bug or overload in the consumer can't take down the producer. The worst case is a backlog in the queue, not a cascading failure up into the request path.

The mental test for "should this be decoupled with a queue": *if the downstream disappeared for 30 seconds, should the user-facing request fail, or should the work just wait?* If "wait," you want a queue.

## Load balancing across services — the competing consumers pattern

The second big reason, and the one direct calls can't give you cheaply. A single queue can be drained by **many consumer instances at once**, and Service Bus hands each message to **exactly one** of them. This is the **competing consumers pattern**: N workers all listening to the same `orders` queue, each grabbing the next available message, so the total throughput scales with the number of workers — automatic load balancing, with zero load-balancer configuration.

How it works, and why it's safe:

- When a consumer picks up a message, Service Bus puts a **lock** on it (PeekLock mode) for a lock duration. While locked, **no other consumer can see or take that message** — so two workers never process the same order. The consumer completes the message (lock released, message gone) or it throws/times out (lock expires, message reappears for someone else).
- Scaling is just "add more consumers." In this repo the consumer is a **Function App on a Consumption plan**, which does this for you: the Functions runtime watches the queue depth and **automatically spins up more instances when the backlog grows**, each an independent competing consumer, then scales back down (to zero) when the queue drains. You don't write or configure the load balancing — the queue + the platform's auto-scale *is* the load balancer.
- Contrast with a plain HTTP load balancer: an HTTP LB spreads *inbound requests* across instances and needs all instances healthy and reachable *now*. Queue-based load balancing spreads *stored work* across whatever consumers happen to be running, pulling at their own pace — a slow or briefly-dead worker simply pulls fewer messages; it doesn't drop any.

One caveat worth naming: plain competing consumers gives you **throughput scaling but not ordering** — if order matters, you use **sessions** (a session id pins all of one group's messages to one consumer, preserving order within that group while still load-balancing across groups). That's the trade-off between "drain as fast as possible" and "process this customer's events strictly in order."

## How this repo implements it — the publisher

**`src/WebApp/Services/ServiceBusPublisher.cs`** + **`src/WebApp/Controllers/OrdersController.cs`**:
```csharp
var message = new OrderCreatedMessage(orderId, customer, amount, DateTime.UtcNow);
await _publisher.PublishAsync("orders", message);
return Accepted(new { message.OrderId, Status = "Published to Service Bus, awaiting processing" });
```
Two details worth remembering because they're easy to get backwards:
- **`ServiceBusClient` is a singleton; `ServiceBusSender` is cheap and per-queue.** The client holds the actual network connection and is expensive to create — you want exactly one for the app's lifetime. A sender is lightweight, so this repo caches one per queue name in a `ConcurrentDictionary` rather than creating a client *or* a sender per call.
- **The controller returns `202 Accepted`, not `200 OK`.** The order hasn't been processed — it's been *queued* for processing. `202` is the honest HTTP status for "I've accepted this, work is happening asynchronously, don't expect a final result in this response."

## How this repo implements it — the receiver

**`src/Functions/OrderCreatedFunction.cs`**:
```csharp
[Function("OrderCreatedFunction")]
public void Run([ServiceBusTrigger("orders", Connection = "ServiceBusConnection")] string messageBody)
{
    var order = JsonSerializer.Deserialize<OrderCreatedMessage>(messageBody)!;
    _logger.LogInformation("Processing order {OrderId} for {Customer}...", order.OrderId, order.Customer);
}
```
This is deliberately in the **separate Functions project**, not the WebApp — in a real system these are two independently deployable Azure resources (an App Service and a Function App), and the whole point of the queue is that neither needs to know the other is up. Notice `OrderCreatedMessage` is **redefined** in the Functions project rather than shared via a project reference — see the note in that file. The wire contract (the JSON shape) is the actual contract; sharing a C# type across a deployment boundary would recouple two things a queue exists specifically to decouple.

## The SDK types — Client, Sender, Receiver, Processor

Four types in **`Azure.Messaging.ServiceBus`** do all the work. Knowing what each is
for (and its lifetime) is a very common interview drill.

> **Use the right SDK.** `Azure.Messaging.ServiceBus` is the current package (the one
> this repo uses). The older `Microsoft.Azure.ServiceBus` (and the even older
> `WindowsAzure.ServiceBus`) are **deprecated** — don't learn or use them for new
> work; the type names and patterns below are the current SDK.


| Type | Role | Lifetime | Direction |
|---|---|---|---|
| **`ServiceBusClient`** | The connection factory — owns the actual AMQP connection + auth to the namespace. You create senders/receivers/processors *from* it. | **Singleton** (expensive; one per app) | — |
| **`ServiceBusSender`** | Sends messages to one queue/topic. | Cheap; cache one per destination | Send |
| **`ServiceBusReceiver`** | Pulls messages **manually** — you call `ReceiveMessagesAsync` in your own loop and complete/abandon them yourself. | Cheap; per consumer | Receive (pull) |
| **`ServiceBusProcessor`** | **Event-driven** receiver — you hand it message + error handlers and call `StartProcessingAsync`; it runs its own loop, manages concurrency, and auto-renews locks. | Cheap; long-lived while processing | Receive (push-style) |

### `ServiceBusClient` — the one you make a singleton
It holds the real network connection and is expensive to construct, so you create
**exactly one** for the app's lifetime and register it as a singleton. Everything
else is created from it. This repo registers it in `Program.cs`:
```csharp
builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnectionString));
```

### `ServiceBusSender` — send
Obtained via `client.CreateSender("orders")`. Lightweight, so you cache one per
queue rather than creating one per publish. This repo (`ServiceBusPublisher.cs`)
caches senders in a `ConcurrentDictionary`:
```csharp
var sender = _senders.GetOrAdd(queueName, _client.CreateSender);
await sender.SendMessageAsync(new ServiceBusMessage(body) { ContentType = "application/json", MessageId = ... });
```
Two sender features worth knowing by name:
- **Batching** — send many messages in one network call. `SendMessagesAsync(IEnumerable)`
  is the simple form; `CreateMessageBatchAsync()` gives a `ServiceBusMessageBatch` you
  `TryAddMessage` into (it refuses once the batch hits the size limit, so you can't
  overflow a single send). Far more efficient than a `SendMessageAsync` per message.
- **Scheduled messages** — deliver a message *later* instead of now, two equivalent ways:
  set `message.ScheduledEnqueueTime = DateTimeOffset.UtcNow.AddMinutes(10)` and send
  normally, or call `sender.ScheduleMessageAsync(message, enqueueTime)` (which returns a
  **sequence number** you can pass to `CancelScheduledMessageAsync` to cancel it before
  it fires). Use it for "process this in 10 minutes", delayed retries, reminders, etc.

### `ServiceBusReceiver` — manual (pull) receive
You control the loop: ask for messages, process, then explicitly settle each one.
It has two **receive modes** — **PeekLock** (default: lock the message, settle it
yourself, at-least-once) and **ReceiveAndDelete** (removed on receive, no settling,
at-most-once) — plus a non-destructive **`PeekMessageAsync`** to inspect without
consuming. Full comparison + code in `docs/SERVICE_BUS_RECEIVER_CONSOLE.md`.
Use it when you want fine-grained control over *when* and *how many* you pull:
```csharp
ServiceBusReceiver receiver = client.CreateReceiver("orders");
ServiceBusReceivedMessage msg = await receiver.ReceiveMessageAsync();
try {
    // ... handle msg.Body ...
    await receiver.CompleteMessageAsync(msg);      // done — remove from queue
} catch {
    await receiver.AbandonMessageAsync(msg);        // release lock -> redelivered
    // or receiver.DeadLetterMessageAsync(msg) to send it straight to dead-letter
}
```

### `ServiceBusProcessor` — event-driven receive (the usual choice for a long-running consumer)
You don't write the loop — you subscribe handlers and start it. It manages
concurrency (`MaxConcurrentCalls`), auto-renews message locks for long work, and
can auto-complete. Best for a hosted service that continuously drains a queue:
```csharp
ServiceBusProcessor processor = client.CreateProcessor("orders", new ServiceBusProcessorOptions { MaxConcurrentCalls = 5 });
processor.ProcessMessageAsync += async args => {
    // ... handle args.Message.Body ...
    await args.CompleteMessageAsync(args.Message);
};
processor.ProcessErrorAsync += args => { /* log args.Exception */ return Task.CompletedTask; };
await processor.StartProcessingAsync();
```

**Receiver vs. Processor — which to pick:** `ServiceBusReceiver` when you want
manual, on-demand control (pull a batch, process, stop). `ServiceBusProcessor` for
a continuously-running consumer — it's higher-level and handles the loop,
concurrency, and lock renewal for you. **In this repo neither is used directly on
the receive side** — the Functions **`[ServiceBusTrigger]`** wraps this machinery
entirely: the Functions host runs the processor for you and just calls your method
per message (auto-complete on success, retry on throw). The Receiver/Processor
snippets above are the equivalent you'd write in a non-Functions consumer (a
console app or a `BackgroundService` in the WebApp).

## Message anatomy — properties, filters & correlation

### A message is body + system properties + application properties

A `ServiceBusMessage` has three parts:
- **Body** — the actual payload (bytes/text/JSON). The "content."
- **System (broker) properties** — a fixed set Service Bus itself understands:
  `MessageId`, `CorrelationId`, `Subject` (a.k.a. Label), `SessionId`, `ContentType`,
  `TimeToLive`, `ReplyTo`, `To`, etc. These drive built-in behavior (dedup keys on
  `MessageId`, sessions key on `SessionId`, and so on).
- **Application properties** — a free-form `IDictionary<string, object>` of your own
  key/value metadata (`ApplicationProperties`). This travels *with* the message as
  headers, **separate from the body**.

The reason application properties matter: **a subscription filter can route on them
without the broker ever reading your body.** You put the routing-relevant facts
("region", "priority", "amount") in properties, and the body stays opaque.

### Adding custom properties and sending

```csharp
var message = new ServiceBusMessage(body)
{
    ContentType   = "application/json",
    MessageId     = order.OrderId.ToString(),   // dedup key (see Duplicate detection)
    Subject       = "OrderCreated",             // a short "type"/category label
    CorrelationId = correlationId               // for correlating related messages
};

// custom metadata — routable, and readable by the receiver without parsing the body:
message.ApplicationProperties["region"]   = "US";
message.ApplicationProperties["priority"] = "high";
message.ApplicationProperties["amount"]   = 42.50;

await sender.SendMessageAsync(message);
```
On the receive side you read them back: `received.ApplicationProperties["region"]`,
`received.Subject`, `received.CorrelationId`, etc. (Runnable in
`samples/ServiceBusSenderConsole`.)

### How subscription filters use properties

Topic **subscriptions** can carry a **filter** so a subscription only receives the
messages it cares about (see the topic/subscription building blocks above). There
are three filter kinds:

- **SQL filter** — a SQL-92-like boolean expression evaluated against the message's
  **system + application properties** (not the body). Most flexible:
  ```sql
  amount > 1000 AND region = 'US'
  ```
  A subscription with that filter only gets messages whose `ApplicationProperties`
  satisfy it. (You can also modify properties on match with a **SQL rule action**,
  e.g. `SET priority = 'escalated'`.)
- **Correlation filter** — matches on specific system properties (commonly
  `CorrelationId` and/or `Subject`) and/or exact-match application properties. It's
  a set of equality checks, not a full expression — **more efficient** than a SQL
  filter, so prefer it when simple equality is all you need:
  ```
  Subject = 'OrderCreated'  (a correlation filter on the Subject/Label)
  ```
- **Boolean filter** — `TrueFilter` (receive everything — the default a subscription
  gets if you don't specify one) or `FalseFilter` (receive nothing).

Key point: filters evaluate **properties, not the body** — which is exactly why you
lift routing-relevant data up into properties. (For the full interview-depth
treatment — rules, the `$Default` rule, rule actions, the multi-rule duplicate
gotcha, and creating filters via portal/CLI/SDK — see `docs/SERVICE_BUS_FILTERS.md`.) Example fan-out: publish "order
placed" to a topic once, and a `large-orders` subscription filters
`amount > 1000` while an `all-orders` subscription takes everything — same message,
different subscriptions matching on the `amount` property.

### Correlation in Service Bus

"Correlation" = tying related messages together. Two levels:
- **`CorrelationId` (system property)** — the canonical field for it. In a
  **request/reply** pattern, a sender sets `ReplyTo` (the queue it wants the answer
  on) and a `CorrelationId`; the responder copies that same `CorrelationId` onto its
  reply, so the original sender can match the reply to the request it sent (it may
  have many in flight). Correlation **filters** on a subscription then route by that
  id/subject.
- **Correlation vs. the body** — put correlation data in the **`CorrelationId`
  property**, not buried in the body. The broker (and subscription filters) can act
  on a property without deserializing your payload; data only in the body is
  invisible to routing and to anyone inspecting the message envelope. Same principle
  as `region`/`amount` above — anything the *infrastructure* needs to act on belongs
  in properties; the body is for the *application's* payload.
- **End-to-end tracing** — this is a different but related "correlation": tools like
  Application Insights stamp their own operation/trace id to follow a message across
  services (see `docs/APPLICATION_INSIGHTS.md`). Service Bus's `CorrelationId` is
  application-level message correlation; the tracing id is diagnostic correlation —
  don't conflate the two in an interview.

## Completing vs. abandoning vs. dead-lettering — the part everyone forgets

- The trigger method **returns normally** → the message is marked complete and removed from the queue. Done, forever.
- The trigger method **throws** → the message becomes available again after a lock-duration timeout, and Service Bus redelivers it — up to `MaxDeliveryCount` times (10 by default).
- After `MaxDeliveryCount` failed deliveries, the message moves to the **dead-letter subqueue** instead of being retried forever or silently dropped — a separate place you can inspect "messages that consistently failed to process," which is the difference between a transient blip and a message that's fundamentally broken (bad data, a bug that always throws on this input).

When you receive manually (`ServiceBusReceiver`, not a trigger), you settle each message explicitly with one of **four** outcomes:
- **Complete** (`CompleteMessageAsync`) — success; remove it from the queue.
- **Abandon** (`AbandonMessageAsync`) — release the lock so it's redelivered (a retry); increments the delivery count, and after `MaxDeliveryCount` it auto-dead-letters. Abandon means "retry," not "discard."
- **Defer** (`DeferMessageAsync`) — set it aside without removing it; it leaves the normal delivery flow and can only be retrieved later **by its sequence number** (`ReceiveDeferredMessageAsync`). Used for out-of-order or not-yet-ready messages you don't want to lose.
- **Dead-letter** (`DeadLetterMessageAsync`) — move it to the dead-letter subqueue immediately, skipping retries, for messages you know are unprocessable. (Messages also arrive here automatically on max-delivery-count or TTL expiry.)

The dead-letter queue (DLQ) itself is a real subqueue you can receive from — you open a receiver on the queue's `$DeadLetterQueue` path to inspect/replay parked messages. See `docs/SERVICE_BUS_RECEIVER_CONSOLE.md` for the settle-options table with code.

## Sending and receiving messages from a queue in Azure

Three ways, from "click in the portal" to "the app's own code":

> For a full click-by-click portal walkthrough (create namespace → create queue →
> send/receive → get the connection string), see `docs/SERVICE_BUS_PORTAL_GUIDE.md`.

### 1. Portal — Service Bus Explorer (no code, fastest to try)
In the Azure Portal, open the namespace → the `orders` queue → **Service Bus
Explorer** (left menu). From there you can:
- **Send**: click *Send messages*, type a body (e.g. the `OrderCreatedMessage`
  JSON), and send — useful for triggering the receiver without running the WebApp.
- **Receive/Peek**: *Peek* looks at messages without removing them; *Receive*
  pulls them (in ReceiveAndDelete or PeekLock mode). This is also where you inspect
  the **dead-letter** subqueue to see messages that failed processing.

### 2. az CLI (create the queue; keys for a connection string)
Message send/receive itself is done via the SDK or the portal, but the CLI is how
you create the queue and get the connection string the senders/receivers need
(full script: `scripts/05-service-bus.azcli`):
```bash
az servicebus queue create --name orders --namespace-name "$SB_NAMESPACE" --resource-group "$RG"
az servicebus namespace authorization-rule keys list \
  --resource-group "$RG" --namespace-name "$SB_NAMESPACE" \
  --name RootManageSharedAccessKey --query primaryConnectionString -o tsv
```

### 3. In code — the SDK (what this repo does)
This is the real send/receive, using `Azure.Messaging.ServiceBus`.

**Send** (`ServiceBusPublisher.cs`) — a `ServiceBusSender` obtained from the
singleton `ServiceBusClient`:
```csharp
ServiceBusSender sender = _client.CreateSender("orders");         // cached per queue
var message = new ServiceBusMessage(JsonSerializer.Serialize(order));
await sender.SendMessageAsync(message);                            // message now durably on the queue
```

**Receive** — two styles:
- *This repo's style* — let the **Functions `[ServiceBusTrigger]`** receive for you
  (`OrderCreatedFunction.cs`); the runtime pulls messages, hands them to your
  method, and auto-completes on success / retries on throw. No manual receive loop.
- *Manual style* (a console app or WebApp, for reference) — a `ServiceBusProcessor`:
  ```csharp
  ServiceBusProcessor processor = _client.CreateProcessor("orders", new ServiceBusProcessorOptions());
  processor.ProcessMessageAsync += async args =>
  {
      string body = args.Message.Body.ToString();
      // ... handle it ...
      await args.CompleteMessageAsync(args.Message);   // remove from queue on success
  };
  processor.ProcessErrorAsync += args => { /* log */ return Task.CompletedTask; };
  await processor.StartProcessingAsync();
  ```
  `CompleteMessageAsync` = done (removed). Not completing (throw/timeout) = redelivered,
  then dead-lettered after `MaxDeliveryCount` — see the next section.

The message flow end-to-end in this repo: `POST /api/orders` → `ServiceBusPublisher`
sends to the `orders` queue → the queue holds it → `OrderCreatedFunction` receives
and processes it. Producer and consumer never talk directly — only through the queue.

## Duplicate detection

**The problem:** networks are unreliable, so a publisher sometimes can't tell if a
send actually succeeded and **retries** — potentially putting the *same* message on
the queue twice. Duplicate detection makes Service Bus discard the second copy so
the consumer only sees it once ("exactly-once *submission*").

**How it works:** enable it on the queue/topic at **creation** (it can't be turned
on later) — `RequiresDuplicateDetection = true` — and set a **duplicate-detection
history time window** (default 10 minutes, configurable). Within that window,
Service Bus tracks the **`MessageId`** of every message; if a new message arrives
with a `MessageId` it has already seen, the new one is **silently dropped**
server-side. The window is a trade-off: longer = catches duplicates further apart
but costs more tracking overhead/throughput.

**The catch that trips people up:** dedup keys on **`MessageId`**, so it only works
if you set a *deterministic, business-meaningful* `MessageId` (e.g. the order id).
This repo's `ServiceBusPublisher` currently sets `MessageId = Guid.NewGuid()` —
a **new** id per call, so every message is unique and dedup would never trigger.
To use duplicate detection you'd set something like `MessageId = orderId.ToString()`
so a retried send of the same order carries the same id and gets deduped.

```csharp
// enabling it (provisioning): az servicebus queue create ... --enable-duplicate-detection true
// and set a deterministic MessageId when sending:
var msg = new ServiceBusMessage(body) { MessageId = order.OrderId.ToString() };
```

## Cross-entity transactions

**Transactions** let you group several Service Bus operations into one **atomic**
unit — either all succeed or all roll back, nothing partial. The classic need: a
consumer wants to **receive/complete a message from one queue AND send a resulting
message to another** as a single all-or-nothing step, so you can't end up having
completed the input but lost the output (or vice versa).

By default a transaction can only span operations on a **single entity**. To span
**multiple** entities (complete on queue A + send to queue B/topic), you opt in with
**`EnableCrossEntityTransactions = true`** on the client, and wrap the operations in
a `TransactionScope`:

```csharp
var client = new ServiceBusClient(connectionString,
    new ServiceBusClientOptions { EnableCrossEntityTransactions = true });

var receiver = client.CreateReceiver("orders");
var sender   = client.CreateSender("orders-processed");

ServiceBusReceivedMessage msg = await receiver.ReceiveMessageAsync();

using (var ts = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
{
    await receiver.CompleteMessageAsync(msg);                       // remove from queue A
    await sender.SendMessageAsync(new ServiceBusMessage("done"));   // send to queue B
    ts.Complete();   // commit BOTH; if this line isn't reached, BOTH roll back
}
```

**Constraints worth knowing:** all entities must live in the **same namespace**, and
with cross-entity transactions enabled the **first entity you interact with becomes
the "send-via" entity** that the others are routed through (an implementation detail
of how Service Bus coordinates the atomic operation across entities). Transactions
are a Standard/Premium feature (not Basic).

> Cross-entity transactions give atomicity *within* Service Bus. For consistency
> across **separate services/databases** — where no shared transaction exists —
> you need the **Saga pattern** instead: see `docs/SAGA_PATTERN.md`.

## Sessions — FIFO ordering and message grouping

By default, competing consumers pull in parallel, so **ordering isn't guaranteed**
(see load balancing above). **Sessions** are how you get strict **FIFO order for a
related group** of messages while still load-balancing across groups.

- Enable it on the queue/subscription at creation: **`RequiresSession = true`**
  (can't be changed later).
- The publisher stamps each message with a **`SessionId`** (e.g. the customer id,
  the order id — whatever defines "these must be in order relative to each other").
- A **session receiver** locks an *entire session* to itself and processes that
  session's messages in strict order: `client.AcceptNextSessionAsync(queueName)`
  gives you a `ServiceBusSessionReceiver` bound to one session; the
  `ServiceBusProcessor` equivalent is a **`ServiceBusSessionProcessor`**
  (`CreateSessionProcessor`).
- Different sessions still process **in parallel** across receivers — you get
  ordering *within* a session and throughput *across* sessions. Sessions also give
  you a per-session **state** store (`SetSessionStateAsync`) for stateful workflows.

Sessions need **Standard or Premium** (not Basic). Trade-off: a session is handled
by one receiver at a time, so a single hot session can't be parallelized — choose a
`SessionId` with enough spread.

## Lock duration, renewal, and idempotency

**Lock duration** — in PeekLock mode, a received message is locked for the queue's
**lock duration** (default 30s, max 5 min). You must settle it before the lock
expires, or Service Bus assumes you failed, releases it, and **redelivers** it
(incrementing delivery count).

**Lock renewal** — if processing legitimately takes longer than the lock duration,
don't let the lock lapse:
- Manually: `receiver.RenewMessageLockAsync(message)` to extend it.
- Automatically: `ServiceBusProcessor` **auto-renews** locks up to
  `MaxAutoLockRenewalDuration` (a `ServiceBusProcessorOptions` setting) — the usual
  reason to prefer the processor for long-running work over a manual receiver.
- Symptom of getting this wrong: a `ServiceBusException` with reason
  `MessageLockLost` when you finally try to complete.

**Idempotency (because delivery is at-least-once)** — Service Bus guarantees
*at-least-once* delivery: a message **can be delivered more than once** (a redelivery
after a lock timeout, a crash after processing but before completing, etc.). So your
**receiver logic must be idempotent** — processing the same message twice must not
double-charge, double-ship, or double-insert. Typical techniques: dedupe on a
business key / the `MessageId`, use "insert if not exists" / upserts, or make the
operation naturally idempotent. This is a favorite interview point: *exactly-once
processing isn't something the broker gives you — you achieve it with at-least-once
delivery plus an idempotent consumer.*

## Auto-forwarding

**Auto-forwarding** chains entities so messages sent to one queue/subscription are
**automatically moved to another** queue or topic in the same namespace, with no code
in between (`ForwardTo` on the entity). Uses:
- Fan a **topic subscription** straight into a downstream **queue** a service already
  reads, so that service doesn't need to know about the topic.
- Build scatter-gather / aggregation topologies (many subscriptions → one queue).

It's a configuration property, not a runtime API — set `ForwardTo` (and optionally
`ForwardDeadLetteredMessagesTo`) when creating the entity. Adds a small latency hop;
there are limits on chain depth.

## Using Service Bus from Azure Functions (bindings)

This repo's receiver is the Functions **`[ServiceBusTrigger]`** (`OrderCreatedFunction`),
which is the most exam-relevant integration. Two things to know:
- **Auto-complete:** by default the Functions Service Bus trigger **completes the
  message automatically** when your function returns without throwing, and abandons
  (→ eventual dead-letter) if it throws. To settle messages yourself, turn
  auto-complete **off** (`autoCompleteMessages = false` in host config / binding) and
  complete/abandon/dead-letter explicitly via the `ServiceBusMessageActions`
  parameter. Knowing the default (auto-complete on) vs. how to opt out is a common
  question.
- **Output binding:** a function can also *send* to Service Bus via an output binding
  (return value / `[ServiceBusOutput]`) instead of constructing a `ServiceBusSender`
  by hand — the binding handles the client for you.

## What's real vs. reference-only in this repo

The publisher and receiver code are both correct and match the real Azure Service Bus SDK surface exactly as they'd be used against a real namespace. Neither has been run end-to-end here — Azurite (used for Blob Storage) does **not** emulate Service Bus, and Microsoft's Service Bus emulator requires Docker, which wasn't available in this environment. The receiver's registration with the Functions host *was* confirmed (it correctly reported "connection string not configured" rather than crashing the whole host — see `docs/FUNCTION_APPS.md`), which at least proves the trigger attribute and method signature are valid.

---

## Interview Questions

**Q: Why put a queue between the controller and the actual order processing instead of just calling it directly?**
Decoupling and resilience. The HTTP caller gets a fast response regardless of how slow downstream processing is; if the receiver is temporarily down, messages just wait in the queue instead of the request failing; and the two sides can be scaled, deployed, and fail independently.

**Q: Walk through the main Service Bus SDK types and their lifetimes.**
`ServiceBusClient` owns the connection to the namespace — expensive, so it's a singleton created once. From it you create: `ServiceBusSender` (sends to a queue/topic; cheap, cache one per destination), `ServiceBusReceiver` (manual pull-based receive — you call `ReceiveMessagesAsync` and settle messages yourself), and `ServiceBusProcessor` (event-driven receive — you register message/error handlers and it runs the loop, manages concurrency, and renews locks). Client = singleton; sender/receiver/processor = created from it and comparatively cheap.

**Q: ServiceBusReceiver vs. ServiceBusProcessor — when would you use each?**
`ServiceBusReceiver` is manual and pull-based: you decide when to receive, how many, and explicitly complete/abandon/dead-letter each — good when you want tight control or batch-at-a-time processing. `ServiceBusProcessor` is the higher-level, event-driven option for a long-running consumer: you give it handlers and call `StartProcessingAsync`, and it owns the receive loop, concurrency (`MaxConcurrentCalls`), and automatic lock renewal. For a continuously-draining background consumer you'd normally reach for the Processor; for on-demand or controlled pulls, the Receiver. (In this repo the Functions `[ServiceBusTrigger]` wraps all of this, so you write neither directly.)

**Q: Why is ServiceBusClient a singleton but senders are created per queue?**
The client owns the actual connection/authentication and is expensive to set up — you want one for the app's whole lifetime, not one per request. Senders are lightweight wrappers around a specific queue/topic and are safe and cheap to keep cached per destination; creating a brand new sender (or client) on every publish would be wasteful and, at scale, could exhaust connections.

**Q: What does 202 Accepted mean here, and why not 200 OK?**
200 implies the request is fully done. 202 means "accepted for processing, but not yet complete" — accurate here, since publishing to the queue doesn't mean the order has actually been processed, only that a message describing it now exists and something will get to it.

**Q: What happens if the receiver throws an exception while processing a message?**
The message is not completed, so it isn't removed from the queue. Service Bus makes it available again after the lock duration expires and redelivers it, up to `MaxDeliveryCount` (10 by default) times. After that many failed attempts, it's moved to the dead-letter subqueue instead of retrying forever.

**Q: What's the dead-letter queue for, and why does it matter operationally?**
It's where messages land after exceeding the max delivery count — a holding area for "things that consistently fail," separate from the live queue. Without it, a poison message (bad data that always throws) would either block/spin the queue forever or get silently lost after retries. With it, you have a concrete place to go inspect exactly which messages are failing and why, without them clogging normal processing.

**Q: Why is OrderCreatedMessage defined twice — once in WebApp, once in Functions — instead of shared in a common project?**
Because WebApp and Functions are two independently deployable Azure resources connected only by the queue, the same way two microservices are. Sharing the actual C# type via a project/package reference would recouple their deployment lifecycles — you couldn't change one without potentially needing to redeploy the other. The JSON shape is the real contract between them; each side owning its own type that happens to match that shape keeps them genuinely independent.

**Q: What's the difference between a namespace, a queue, a topic, and a subscription?**
A namespace is the top-level container and endpoint (the "server"), holding queues and topics and setting the pricing tier. A queue is point-to-point: each message goes to exactly one consumer. A topic is publish/subscribe: it fans each message out to all of its subscriptions. A subscription is a named virtual queue on a topic that receives its own independent copy of each message (optionally filtered) and is consumed independently. Short version: queue = one message → one consumer; topic+subscriptions = one message → every subscription.

**Q: When would you use a topic instead of a queue?**
When several *different* services each independently need to react to the same event. "Order placed" that should be handled once → queue. "Order placed" that should trigger fulfillment AND analytics AND an email, each a separate service with its own copy → topic with one subscription per service. Subscription filters let each subscription receive only the messages it cares about.

**Q: How does Service Bus load-balance work across multiple consumers?**
The competing consumers pattern: multiple consumer instances all listen to the same queue, and Service Bus locks each message to exactly one of them (PeekLock) while it's being processed, so no two consumers handle the same message. Throughput scales with the number of consumers, and with a Consumption-plan Function App the platform auto-scales instances up as the queue backlog grows and back down as it drains — the queue plus platform auto-scale is the load balancer, with nothing to configure.

**Q: What's the difference between queue-based load balancing and an HTTP load balancer?**
An HTTP load balancer distributes *inbound requests* across instances that must all be healthy and reachable at that moment. Queue-based load balancing distributes *stored work*: consumers pull messages at their own pace, so a slow or briefly-down worker just pulls fewer messages rather than dropping any, and work survives in the queue until someone processes it. One balances live traffic; the other balances durable work.

**Q: Does competing consumers preserve message order?**
No — with plain competing consumers, multiple workers pull in parallel, so ordering isn't guaranteed. If you need ordering, use sessions: a session id pins all of one group's messages to a single consumer so they're processed in order, while different sessions still load-balance across consumers. It's a deliberate trade-off between maximum throughput and per-group ordering.

**Q: What are application properties on a message, and why not just put everything in the body?**
Application properties are a free-form key/value metadata dictionary that travels with the message alongside (not inside) the body. You use them for anything the *infrastructure* needs to act on — subscription filters evaluate properties (system + application), not the body, so lifting routing-relevant facts (region, priority, amount) into properties lets the broker route without ever deserializing your payload. The body stays for the application's actual data.

**Q: What kinds of subscription filters are there and which is most efficient?**
SQL filters (a SQL-92-like boolean expression over the message's properties, most flexible, can also mutate properties via a rule action), correlation filters (equality matches on system properties like CorrelationId/Subject and/or app properties — more efficient than SQL, use when simple equality suffices), and boolean filters (TrueFilter = everything, the default; FalseFilter = nothing). All evaluate properties, never the body.

**Q: How does correlation work in Service Bus?**
Via the `CorrelationId` system property. In request/reply, the sender sets `ReplyTo` (where it wants the answer) and a `CorrelationId`; the responder echoes that `CorrelationId` on the reply so the sender can match the reply to the originating request among many in flight. Correlation filters on subscriptions can also route by `CorrelationId`/`Subject`. Keep correlation data in the property, not the body, so filters and the broker can act on it without parsing the payload — and don't confuse it with Application Insights' diagnostic trace correlation, which is a separate, tracing-level id.

**Q: How do you guarantee ordered processing of related messages?**
Sessions. Enable `RequiresSession = true` on the queue/subscription, stamp related messages with the same `SessionId`, and consume with a session receiver (`AcceptNextSessionAsync`) or `ServiceBusSessionProcessor` — that receiver locks the whole session and processes its messages in strict FIFO order. Different sessions still run in parallel across receivers, so you keep throughput across groups while getting ordering within a group. (Standard/Premium only.)

**Q: A message takes longer to process than the lock duration — what happens and how do you handle it?**
If you don't settle before the lock expires, Service Bus releases the message and redelivers it (you'll get `MessageLockLost` if you then try to complete). Handle it by renewing the lock — `RenewMessageLockAsync` manually, or let `ServiceBusProcessor` auto-renew up to `MaxAutoLockRenewalDuration`. That auto-renewal is a key reason to use the processor for long-running work.

**Q: Service Bus is at-least-once — how do you get exactly-once *processing*?**
You don't get it from the broker; you build it. Because a message can be delivered more than once, make the receiver idempotent — dedupe on a business key or `MessageId`, use upserts/"insert if not exists", or design naturally idempotent operations — so reprocessing the same message has no extra effect. Exactly-once processing = at-least-once delivery + an idempotent consumer.

**Q: What's a scheduled message?**
A message you enqueue now but that becomes available for delivery at a future time — set `ScheduledEnqueueTime` (or `ScheduleMessageAsync`, which returns a sequence number you can use to cancel it). Used for delayed processing, reminders, or delayed retries without holding the message in your own app.

**Q: What is auto-forwarding?**
A configured chain (`ForwardTo`) that automatically moves messages from one queue/subscription to another queue/topic in the same namespace, no code in between — e.g. funneling a topic subscription into a queue a service already consumes. Set at entity creation; adds a small hop and has chain-depth limits.

**Q: With the Functions Service Bus trigger, when does the message get completed?**
By default, automatically: the trigger completes the message when your function returns successfully and abandons it if it throws. To control settlement yourself, disable auto-complete (`autoCompleteMessages = false`) and use the `ServiceBusMessageActions` to complete/abandon/dead-letter explicitly.

**Q: What is duplicate detection and what does it key on?**
It makes Service Bus discard a message whose `MessageId` it has already seen within a configured time window, so a publisher that retries a send (e.g. after a network blip) doesn't cause the consumer to process the same message twice. You enable it at queue/topic creation (`RequiresDuplicateDetection`, can't be changed later) and set the history window (default 10 min). The gotcha: it keys on `MessageId`, so you must assign a deterministic, business-meaningful id (like the order id) — random per-send ids (e.g. `Guid.NewGuid()`) never dedupe.

**Q: What's a cross-entity transaction in Service Bus and when would you use one?**
A transaction that atomically groups operations across *multiple* entities — e.g. completing a message on one queue and sending a message to another queue/topic as a single all-or-nothing unit, so you can't complete the input but lose the output. You enable `EnableCrossEntityTransactions` on the client and wrap the operations in a `TransactionScope`; all entities must be in the same namespace, and the first entity touched becomes the send-via entity. Use it for reliable "receive-and-forward" patterns where a partial result would corrupt state.

**Q: Service Bus vs. Storage Queues — when would you pick one over the other?**
Storage Queues are cheaper and simpler — fine for basic "fire and forget" background work. Service Bus costs more but adds things Storage Queues don't have: guaranteed ordering via sessions, transactions, a proper dead-letter subqueue, and topics/subscriptions (one message delivered to multiple independent subscribers, not just one consumer). Pick Service Bus when you need any of those; Storage Queues if you genuinely just need "a queue" and nothing more.
