# Service Bus Receiver — Minimal Console App (step by step)

The companion to `docs/SERVICE_BUS_SENDER_CONSOLE.md`: a plain console app that
**receives** messages from a queue using `ServiceBusReceiver` (manual pull). Run
the sender to put messages on the `orders` queue, then run this to pull them off.
Runnable version: `samples/ServiceBusReceiverConsole/`.

Theory + the Receiver-vs-Processor comparison: `docs/SERVICE_BUS.md`.

---

## Step 1 — Create the console project

**Visual Studio:** File → New → Project → **Console App** (C#) → name it
`ServiceBusReceiverConsole`.

**Or CLI:**
```bash
dotnet new console -n ServiceBusReceiverConsole
cd ServiceBusReceiverConsole
```

## Step 2 — Install the NuGet package

Same single package as the sender — it covers both send and receive:
**`Azure.Messaging.ServiceBus`**.

**Visual Studio:** right-click project → **Manage NuGet Packages** → search
**Azure.Messaging.ServiceBus** → Install.

**Or CLI:**
```bash
dotnet add package Azure.Messaging.ServiceBus
```

## Step 3 — Get the connection string and queue name

Same two values as the sender:
- **Connection string** — namespace/queue → **Shared access policies** →
  **RootManageSharedAccessKey** → **Primary Connection String**. (For a receiver,
  a **Listen**-only policy is enough — least privilege.)
- **Queue name** — `orders` (from the namespace's Queues list).

(See `docs/SERVICE_BUS_PORTAL_GUIDE.md`.)

## Step 4 — Create the client and receiver, then receive

Replace `Program.cs` with this. It creates the `ServiceBusClient` from the
connection string, creates a `ServiceBusReceiver` by **passing the queue name**,
and pulls messages in a loop:

```csharp
using Azure.Messaging.ServiceBus;

// 1) The two values (read the connection string from an env var, not hardcoded).
string connectionString =
    Environment.GetEnvironmentVariable("SERVICE_BUS_CONNECTION_STRING")
    ?? "Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>";

string queueName = "orders";

// 2) Client (owns the connection) + receiver (pass the queue name).
await using ServiceBusClient client = new ServiceBusClient(connectionString);
ServiceBusReceiver receiver = client.CreateReceiver(queueName);

Console.WriteLine($"Receiving from queue '{queueName}'...");

// 3) Pull messages. ReceiveMessageAsync waits up to maxWaitTime, then returns
//    null if nothing arrived — we use that to stop.
while (true)
{
    ServiceBusReceivedMessage? message =
        await receiver.ReceiveMessageAsync(maxWaitTime: TimeSpan.FromSeconds(5));

    if (message is null)
    {
        Console.WriteLine("No more messages. Done.");
        break;
    }

    Console.WriteLine($"Received: {message.Body}");

    // Complete = remove from the queue. (On failure you'd Abandon to retry,
    // or DeadLetter to park it — see below.)
    await receiver.CompleteMessageAsync(message);
}
```

What each piece does:
- **`ServiceBusClient`** — owns the connection to the namespace (built from the
  connection string).
- **`ServiceBusReceiver`** — created via `client.CreateReceiver(queueName)`; the
  object you pull messages from. Default mode is **PeekLock**: a received message
  is *locked* (hidden from other receivers) until you settle it.
- **`ReceiveMessageAsync`** — pulls one message, waiting up to `maxWaitTime`;
  returns `null` when nothing arrives in that window.
- **`CompleteMessageAsync`** — marks it done and removes it from the queue.

### Settling a message — the four outcomes

Once you've received a message (in PeekLock mode), you must decide what happens to
it. There are four ways to settle it:

| Outcome | Method | What it does |
|---|---|---|
| **Complete** | `CompleteMessageAsync` | Processed successfully → **removed from the queue** for good. |
| **Abandon** | `AbandonMessageAsync` | Release the lock and **put it back on the queue for redelivery** (a retry). Increments the delivery count; after `MaxDeliveryCount` failed attempts Service Bus auto-dead-letters it. |
| **Defer** | `DeferMessageAsync` | **Set it aside for later** — it stays in the queue but is no longer delivered in the normal flow. You can only get it back later **by its sequence number** (`ReceiveDeferredMessageAsync(seq)`). |
| **Dead-letter** | `DeadLetterMessageAsync` | Move it **now** to the dead-letter subqueue (DLQ), skipping further retries — for messages you know are bad/unprocessable. |

Plain-English version (your framing, made precise):
- **Complete** — "done with it, delete it." The message is removed from the queue.
- **Abandon** — "I couldn't handle it right now, give it back." It returns to the
  queue and will be redelivered (retried). *(Note: abandon means "retry it," not "I
  don't need it" — if you truly don't want it processed, that's Complete (discard
  quietly) or Dead-letter (park it for inspection).)*
- **Defer** — "not now, but don't lose it — I'll ask for it later by its number."
  Used when a message arrives out of order or depends on something not ready yet;
  you record its sequence number and fetch it specifically when you're ready.
- **Dead-letter** — "this one's broken; park it in the DLQ so it stops clogging the
  queue and someone can inspect it." Also where messages land automatically after
  exceeding `MaxDeliveryCount` or expiring (TTL).

Not settling at all → the lock eventually expires and the message reappears
(counts as a delivery attempt).

## Step 5 — Run it (with the sender)

1. Set the connection string:
   ```powershell
   $env:SERVICE_BUS_CONNECTION_STRING = "Endpoint=sb://...;SharedAccessKey=..."
   ```
2. Run the **sender** first (see `docs/SERVICE_BUS_SENDER_CONSOLE.md`) to put a
   few messages on the queue.
3. Run this **receiver**:
   ```bash
   dotnet run
   ```
   Expected output:
   ```
   Receiving from queue 'orders'...
   Received: Hello from the console at 2026-07-22T...
   No more messages. Done.
   ```
4. Confirm the queue drained: in the portal, the queue's **Active message count**
   drops to 0 as messages are completed.

## ServiceBusReceiver vs. ServiceBusProcessor

This sample uses **`ServiceBusReceiver`** — *manual pull*: you control when and
how many messages you receive, and settle each yourself. That's ideal for a
console/tool or batch job.

For a **long-running** consumer that should continuously drain a queue, prefer
**`ServiceBusProcessor`** instead: you register a message handler + an error
handler and call `StartProcessingAsync`, and it runs the receive loop, manages
concurrency, and auto-renews locks for you. (This repo's actual receiver is the
Functions `[ServiceBusTrigger]`, which wraps a processor for you — see the SDK
types section in `docs/SERVICE_BUS.md`.)

## Troubleshooting

- **Receives nothing / "No more messages" immediately** → the queue is empty (run
  the sender first), or you're pointed at the wrong namespace/queue.
- **`UnauthorizedAccessException`** → the SAS policy lacks **Listen** rights, or a
  bad connection string.
- **`ServiceBusException: MessageLockLost`** → you held a message longer than the
  lock duration before completing it; process faster, renew the lock, or use
  `ServiceBusProcessor` (which auto-renews).

## What's real vs. reference-only in this repo

`samples/ServiceBusReceiverConsole/` **builds cleanly** and matches real SDK usage.
It wasn't *run* here (no Azure Service Bus available + the Smart App Control
restriction in `docs/ARCHITECTURE.md`), but it runs as-written against a real
namespace + queue on a normal machine.

---

## Interview recap

**Q: How do you receive messages with ServiceBusReceiver, and what are the settle options?**
Create a `ServiceBusReceiver` from the client with the queue name, call
`ReceiveMessageAsync` (or `ReceiveMessagesAsync` for a batch), read `message.Body`,
then settle: `CompleteMessageAsync` (done, remove), `AbandonMessageAsync` (release
for retry), or `DeadLetterMessageAsync` (park it). In default PeekLock mode a
message is locked until settled; if you never settle it, the lock expires and it's
redelivered.

**Q: When would you use ServiceBusReceiver vs. ServiceBusProcessor?**
Receiver for manual, on-demand pulling (a tool, batch job, or when you want tight
control). Processor for a continuously-running consumer — it owns the loop,
concurrency, and lock renewal, so you just supply handlers. The Functions
ServiceBusTrigger is a managed processor under the hood.
