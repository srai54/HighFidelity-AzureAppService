# Azure Service Bus — Queue (Publisher + Receiver)

## The plain-English version

A queue solves one problem: **the thing that creates work and the thing that does the work don't need to run at the same time, or even both be up at the same moment.** Without a queue, `OrdersController` would have to call the order-processing logic directly, in-process, and if that processing is slow (charge a card, call a shipping API, write to three tables) the customer's HTTP request sits there waiting for all of it. With a queue, the controller does one fast thing — "drop a message describing what happened" — and returns immediately. Something else, whenever it's ready, picks that message up and does the slow work.

Service Bus specifically (as opposed to Storage Queues, the cheaper/simpler alternative) adds things a basic queue doesn't have: guaranteed FIFO ordering (with sessions), transactions, a dead-letter subqueue for poison messages, and topics/subscriptions for one message to fan out to multiple independent receivers.

## The analogy that makes it click

Think of a restaurant kitchen ticket rail. The waiter (the **publisher** — `OrdersController` in this repo) writes an order on a ticket and clips it to the rail, then immediately goes back to serving other tables — they don't stand at the pass watching it cook. The chef (the **receiver** — `OrderCreatedFunction` in this repo) works through tickets on the rail whenever they're free, in whatever order they come. Crucially: **the waiter never talks to the chef directly.** If the chef is on a break, tickets just wait on the rail — nothing is lost, and the waiter's job (taking orders) is completely unaffected by whether the kitchen is fast or slow right now. That decoupling is the entire value of a queue.

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

## Completing vs. abandoning vs. dead-lettering — the part everyone forgets

- The trigger method **returns normally** → the message is marked complete and removed from the queue. Done, forever.
- The trigger method **throws** → the message becomes available again after a lock-duration timeout, and Service Bus redelivers it — up to `MaxDeliveryCount` times (10 by default).
- After `MaxDeliveryCount` failed deliveries, the message moves to the **dead-letter subqueue** instead of being retried forever or silently dropped — a separate place you can inspect "messages that consistently failed to process," which is the difference between a transient blip and a message that's fundamentally broken (bad data, a bug that always throws on this input).

## What's real vs. reference-only in this repo

The publisher and receiver code are both correct and match the real Azure Service Bus SDK surface exactly as they'd be used against a real namespace. Neither has been run end-to-end here — Azurite (used for Blob Storage) does **not** emulate Service Bus, and Microsoft's Service Bus emulator requires Docker, which wasn't available in this environment. The receiver's registration with the Functions host *was* confirmed (it correctly reported "connection string not configured" rather than crashing the whole host — see `docs/FUNCTION_APPS.md`), which at least proves the trigger attribute and method signature are valid.

---

## Interview Questions

**Q: Why put a queue between the controller and the actual order processing instead of just calling it directly?**
Decoupling and resilience. The HTTP caller gets a fast response regardless of how slow downstream processing is; if the receiver is temporarily down, messages just wait in the queue instead of the request failing; and the two sides can be scaled, deployed, and fail independently.

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

**Q: Service Bus vs. Storage Queues — when would you pick one over the other?**
Storage Queues are cheaper and simpler — fine for basic "fire and forget" background work. Service Bus costs more but adds things Storage Queues don't have: guaranteed ordering via sessions, transactions, a proper dead-letter subqueue, and topics/subscriptions (one message delivered to multiple independent subscribers, not just one consumer). Pick Service Bus when you need any of those; Storage Queues if you genuinely just need "a queue" and nothing more.
