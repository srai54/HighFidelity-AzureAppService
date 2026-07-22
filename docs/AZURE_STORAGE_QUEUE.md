# Azure Storage Queues

Part of the storage account (alongside Blob/File/Table). A simple, cheap message
queue. For the richer broker, see `docs/SERVICE_BUS.md`; for the "which one" decision,
`docs/MESSAGING_COMPARISON.md`.

## Intro — what it is

A **Storage Queue** is a basic FIFO-ish queue for passing messages between components:
a producer adds a message, a consumer pulls it, processes it, and deletes it. It's the
**no-frills** option — part of every storage account, dirt cheap, and dead simple.

Message flow, and the one twist that surprises people:
1. **Enqueue** — producer adds a message (a string / small payload, ≤ 64 KB).
2. **Dequeue (get)** — consumer receives a message; it becomes **invisible** to other
   consumers for a **visibility timeout** (default 30s) instead of being deleted.
3. **Delete** — consumer **must explicitly delete** the message after processing.
4. If the consumer *doesn't* delete it within the visibility timeout (it crashed,
   took too long), the message becomes **visible again** and another consumer gets it.

That get-then-delete pattern is the whole reliability model — it's how a crashed
consumer doesn't lose the message (see "delete" below).

## Advantages of Storage Queues

- **Simple & cheap** — no separate resource to provision (it's in the storage account
  you likely already have), and priced at storage rates — far cheaper than Service Bus
  for high volume of simple messages.
- **Huge & durable** — a queue can hold **millions** of messages, up to the storage
  account's capacity (Service Bus queue size is bounded by tier). Good when a big
  backlog can build up.
- **Simple REST/SDK access** — easy to use from anything.
- **Good enough** for straightforward "hand a job to a background worker" scenarios
  that don't need ordering guarantees, transactions, topics, sessions, or
  dead-lettering.

**When NOT to use it (pick Service Bus instead):** you need FIFO ordering, sessions,
duplicate detection, transactions, topics/subscriptions (pub-sub), a built-in
dead-letter queue, or messages larger than 64 KB. Storage Queues are the "just a
queue" option; Service Bus is the "enterprise messaging" option.

## The fan-out pattern

"Fan-out" = spread a large batch of work across **many workers running in parallel**.
Storage Queues are a classic backbone for it:
- A producer drops **many messages** onto one queue (one per work item).
- **Multiple consumer instances** all read from the same queue; each grabs different
  messages (the visibility timeout keeps two workers off the same message), so the
  work is naturally load-balanced across however many workers are running.
- With **Azure Functions + a Queue trigger**, this is automatic: the Functions
  Consumption plan **scales out instances based on queue length**, so a sudden pile of
  messages spins up more parallel workers, then scales back to zero when the queue
  drains — fan-out with no load balancer to configure. (See the Functions triggers
  reference in `docs/FUNCTION_APPS.md`.)

Fan-out/fan-in (spread work, then aggregate results once all complete) is also a
Durable Functions pattern — see `docs/DURABLE_FUNCTIONS.md`; the difference is Durable
Functions gives you the "wait for all and combine" half, whereas a raw queue just
gives you the spread-out half.

## Deleting a message from a Storage Queue

Unlike Service Bus's "complete," a Storage Queue consumer **explicitly deletes** the
message once it's done, using two values returned when the message was received:
- **`MessageId`** — which message.
- **`PopReceipt`** — a token proving you're the consumer currently holding it (it
  changes each time the message is dequeued, so a stale receipt is rejected).

```csharp
QueueClient queue = new QueueClient(connectionString, "jobs");

// receive (message becomes invisible for the visibility timeout):
QueueMessage msg = (await queue.ReceiveMessagesAsync(maxMessages: 1)).Value.First();

// ... process msg.Body ...

// delete it so it's gone for good (must pass BOTH id and pop receipt):
await queue.DeleteMessageAsync(msg.MessageId, msg.PopReceipt);
```
If you **don't** delete before the visibility timeout expires, the message reappears
and is redelivered — which is the safety net (a crashed worker's message isn't lost),
but also means **delivery is at-least-once**, so your processing must be **idempotent**
(same requirement as Service Bus — see `docs/SERVICE_BUS.md`). A message that keeps
failing has a `DequeueCount` you can check to move it aside manually (Storage Queues
have **no built-in dead-letter queue** — that's a Service Bus feature).

## What's real vs. reference-only in this repo

This repo doesn't implement a Storage Queue (it uses Service Bus for messaging and
Blob Storage for files). This is a reference doc — the SDK usage shown is accurate.
Azurite (already used for Blob testing) *does* emulate Storage Queues, so this is
locally testable in principle on an unrestricted machine.

---

## Interview Questions

**Q: Storage Queue vs. Service Bus queue — when do you pick which?**
Storage Queue for simple, cheap, high-volume "just a queue" needs where you don't
require ordering, transactions, sessions, pub-sub, dead-lettering, or messages > 64 KB
— and it can hold millions of messages. Service Bus when you need any of those
enterprise features. Storage Queue = simple/cheap; Service Bus = rich/enterprise.

**Q: How does a Storage Queue avoid losing a message if the consumer crashes?**
Receiving doesn't delete the message — it makes it invisible for a visibility timeout.
The consumer must explicitly delete it after processing. If the consumer crashes
before deleting, the timeout lapses and the message becomes visible again for another
consumer. That's at-least-once delivery, so consumers must be idempotent.

**Q: What's the PopReceipt for?**
It's a token returned on receive that you must pass (with the MessageId) to delete or
update the message. It proves you're the current holder and changes on each dequeue,
so a stale receipt (e.g. after the message reappeared and someone else took it) is
rejected — preventing you from deleting a message you no longer own.

**Q: How do Storage Queues support a fan-out pattern?**
Producers enqueue many work-item messages; multiple consumer instances read the same
queue in parallel, each taking different messages (visibility timeout prevents
overlap), spreading the work. With Azure Functions' Queue trigger, the platform
auto-scales worker instances on queue length, giving parallel fan-out with no manual
load balancing.

**Q: Do Storage Queues have a dead-letter queue?**
No — that's a Service Bus feature. Storage Queues expose a `DequeueCount` per message
so you can detect repeatedly-failing "poison" messages and move them aside yourself
(e.g. to a separate "poison" queue), but there's no automatic dead-lettering.
