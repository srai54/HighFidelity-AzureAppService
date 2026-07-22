# Service Bus Dead-Letter Queue (DLQ)

Covers the three things asked about most: **what a dead-letter queue is**, **how to
send a message to it** (dead-lettering), and **how to read messages back from it**.
Runnable sample: `samples/ServiceBusDeadLetterConsole/`. Related settlement options
(complete/abandon/defer): `docs/SERVICE_BUS_RECEIVER_CONSOLE.md`.

## What is a dead-letter queue?

Every Service Bus queue (and every topic subscription) automatically comes with a
built-in **sub-queue** called the **dead-letter queue (DLQ)** — a side-holding area
for messages that **can't be delivered or processed successfully**. You don't
create it; it exists as part of the queue, addressable at the special path
`<queue-name>/$DeadLetterQueue`.

Think of it as the **"returns bin" or "undeliverable mail" pile**: messages that
couldn't be handled are moved out of the main flow into a clearly separate place,
so they (a) stop clogging the live queue and blocking healthy messages, and (b)
aren't silently lost — someone can go inspect exactly what failed and why.

**How messages land in the DLQ — two ways:**
1. **Automatically**, by Service Bus, when:
   - a message exceeds **`MaxDeliveryCount`** (repeatedly received and abandoned/not
     completed — a "poison message" that keeps failing), or
   - a message **expires** (its time-to-live/TTL elapses before it's processed).
2. **Explicitly**, by your code, when you decide a message is unprocessable and
   call `DeadLetterMessageAsync` (e.g. it fails validation — retrying won't help).

The DLQ is why "abandon" is safe: a bug that always throws on one message doesn't
retry forever — after `MaxDeliveryCount` it's dead-lettered and the queue moves on.

## Sending a message to the dead-letter queue (dead-lettering)

You don't push a *new* message into the DLQ directly — you **dead-letter a message
you received**, which moves it there. Tag it with a reason so triage is possible:

```csharp
ServiceBusReceiver receiver = client.CreateReceiver("orders");
ServiceBusReceivedMessage message = await receiver.ReceiveMessageAsync();

await receiver.DeadLetterMessageAsync(
    message,
    deadLetterReason: "ValidationFailed",
    deadLetterErrorDescription: "Order amount was negative — cannot process.");
```
`deadLetterReason` and `deadLetterErrorDescription` are stored on the message and
readable when you later read the DLQ — this is how you record *why* each message
was rejected instead of just that it was.

## Reading messages from the dead-letter queue

The DLQ is a real sub-queue, so you read it with a normal `ServiceBusReceiver` —
you just tell the client to target the dead-letter sub-queue via
`SubQueue = SubQueue.DeadLetter` (you pass the **same queue name**, not a different
one):

```csharp
ServiceBusReceiver dlqReceiver = client.CreateReceiver(
    "orders",
    new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

ServiceBusReceivedMessage dead = await dlqReceiver.ReceiveMessageAsync();

Console.WriteLine(dead.Body);
Console.WriteLine(dead.DeadLetterReason);            // "ValidationFailed"
Console.WriteLine(dead.DeadLetterErrorDescription);  // "Order amount was negative..."

await dlqReceiver.CompleteMessageAsync(dead);        // removes it from the DLQ
```
Everything you know about receiving from a normal queue applies here — same
complete/abandon settlement, same PeekLock. A common triage flow: read from the
DLQ, inspect the reason, fix the underlying cause, optionally **re-publish a
corrected message to the main queue** (this is called "resubmitting" or
"re-queuing"), then complete the dead-letter copy to clear it.

## How this repo demonstrates it

`samples/ServiceBusDeadLetterConsole/Program.cs` does both halves end to end:
- **Part A** receives a message from the main `orders` queue and dead-letters it
  with a reason (sending it to the DLQ).
- **Part B** opens a `SubQueue.DeadLetter` receiver, reads the message back,
  prints its `DeadLetterReason`/`DeadLetterErrorDescription`, and completes it.

Run the sender sample first (`samples/ServiceBusSenderConsole`) to have a message
to work with, then run this one.

You can also see the DLQ in the **portal**: the queue's **Service Bus Explorer**
has a dead-letter view, and the queue Overview shows a separate **dead-letter
message count** (see `docs/SERVICE_BUS_PORTAL_GUIDE.md`).

## What's real vs. reference-only in this repo

`samples/ServiceBusDeadLetterConsole/` **builds cleanly** and uses the real SDK
API (`DeadLetterMessageAsync`, `SubQueue.DeadLetter`, `DeadLetterReason`). It
wasn't *run* here (no Azure namespace + the Smart App Control restriction in
`docs/ARCHITECTURE.md`), but runs as-written against a real queue.

---

## Interview Questions

**Q: What is the dead-letter queue and what goes into it?**
A built-in sub-queue on every queue/subscription that holds messages which can't be
processed — either automatically (a message that exceeded `MaxDeliveryCount`, i.e.
a poison message, or one whose TTL expired) or explicitly (your code called
`DeadLetterMessageAsync` because the message is invalid). It keeps failures out of
the live queue and preserves them for inspection instead of losing them.

**Q: How do you send a message to the DLQ from code?**
You dead-letter a *received* message with `DeadLetterMessageAsync`, optionally
passing a reason and description. You can't inject a brand-new message straight
into a DLQ — it's populated by moving messages there, either by you or by Service
Bus after max-delivery/TTL.

**Q: How do you read from the DLQ?**
Open a normal `ServiceBusReceiver` on the same queue name but with
`ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter }` (or address the
`<queue>/$DeadLetterQueue` path). Then receive/complete like any queue; the
dead-lettered messages expose `DeadLetterReason` and `DeadLetterErrorDescription`.

**Q: What's the difference between abandon and dead-letter?**
Abandon returns the message to the *main* queue for redelivery (a retry) and bumps
the delivery count. Dead-letter moves it *out* to the DLQ, skipping further retries.
Abandon = "try again later"; dead-letter = "this one's not processable, park it."
Repeated abandons eventually cause an automatic dead-letter once `MaxDeliveryCount`
is hit.

**Q: A message keeps failing and retrying forever — what prevents that?**
`MaxDeliveryCount`. After that many failed delivery attempts (each abandon/timeout
counts), Service Bus automatically dead-letters the message, so a single poison
message can't spin the queue indefinitely or block the messages behind it.
