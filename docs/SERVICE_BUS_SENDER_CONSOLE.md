# Service Bus Sender — Minimal Console App (step by step)

The smallest possible program that sends a message to a Service Bus queue: a
plain console app, no DI, no web host — just the SDK. This is the best way to
learn the raw `ServiceBusClient` → `ServiceBusSender` → `SendMessageAsync` flow
before seeing it wrapped in DI (as in this repo's WebApp). Runnable version lives
in `samples/ServiceBusSenderConsole/`.

Theory: `docs/SERVICE_BUS.md`. Where the namespace/queue come from:
`docs/SERVICE_BUS_PORTAL_GUIDE.md`.

---

## Step 1 — Create the console project

**Visual Studio:** File → New → Project → **Console App** (C#) → name it
`ServiceBusSenderConsole`.

**Or CLI:**
```bash
dotnet new console -n ServiceBusSenderConsole
cd ServiceBusSenderConsole
```

## Step 2 — Install the required NuGet package

Only one package is needed for both sending and receiving: **`Azure.Messaging.ServiceBus`**.

**Visual Studio:** right-click the project → **Manage NuGet Packages** → Browse →
search **Azure.Messaging.ServiceBus** → Install.

**Or CLI:**
```bash
dotnet add package Azure.Messaging.ServiceBus
```
(This repo pins version `7.20.2`.)

## Step 3 — Get the connection string and queue name from the portal

1. **Connection string** — in the Azure Portal, open your namespace (or the
   specific queue) → **Shared access policies** → **RootManageSharedAccessKey** →
   copy **Primary Connection String**. It looks like:
   ```
   Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>=
   ```
2. **Queue name** — open the namespace → **Queues** → copy the queue's name (this
   repo uses **`orders`**).

(See `docs/SERVICE_BUS_PORTAL_GUIDE.md` for creating these if they don't exist,
and the SAS-policy / least-privilege details.)

## Step 4 — Write the code

Replace `Program.cs` with this. It declares the two variables, creates the client
and sender, builds a message, and sends it:

```csharp
using Azure.Messaging.ServiceBus;

// 1) The two values you copied from the portal.
//    Read the connection string from an environment variable so you never
//    hardcode/commit a real secret (see the security note below).
string connectionString =
    Environment.GetEnvironmentVariable("SERVICE_BUS_CONNECTION_STRING")
    ?? "Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>";

string queueName = "orders";

// 2) Create the client (owns the connection) and a sender for the queue.
await using ServiceBusClient client = new ServiceBusClient(connectionString);
ServiceBusSender sender = client.CreateSender(queueName);

// 3) Build and send a message.
ServiceBusMessage message = new ServiceBusMessage($"Hello from the console at {DateTime.UtcNow:O}")
{
    ContentType = "text/plain",
    MessageId = Guid.NewGuid().ToString()
};

await sender.SendMessageAsync(message);

Console.WriteLine($"Sent 1 message to queue '{queueName}'.");
```

What each piece does:
- **`ServiceBusClient`** — owns the actual connection + auth to the namespace.
  Built from the connection string. (In a real app it's a singleton; in a tiny
  console app you just create, use, dispose — `await using` handles disposal.)
- **`ServiceBusSender`** — created from the client via `CreateSender(queueName)`;
  the object you call to send.
- **`ServiceBusMessage`** — the message; its body is your payload (string or bytes),
  plus optional metadata like `ContentType` and `MessageId`.
- **`SendMessageAsync`** — puts the message on the queue. Once it returns, the
  message is durably stored in Service Bus.

### Adding properties to the message

Beyond the body, a message carries **system properties** (`Subject`/Label,
`CorrelationId`, `ContentType`, …) and a free-form **application properties**
dictionary for your own metadata. The runnable sample sets both:
```csharp
var message = new ServiceBusMessage(body)
{
    ContentType   = "text/plain",
    Subject       = "OrderCreated",              // short message "type"/category
    CorrelationId = Guid.NewGuid().ToString()    // correlate related messages
};
message.ApplicationProperties["region"]   = "US";
message.ApplicationProperties["priority"] = "high";
message.ApplicationProperties["amount"]   = 42.50;
```
Why properties (not just the body): **topic subscription filters route on
properties, not the body** — so routing-relevant facts belong here. Full treatment
(system vs application properties, SQL/correlation filters, correlation) is in the
"Message anatomy" section of `docs/SERVICE_BUS.md`.

> **Security:** don't paste a real connection string into the file and commit it.
> The sample reads it from the `SERVICE_BUS_CONNECTION_STRING` environment variable.
> Set it before running:
> ```powershell
> $env:SERVICE_BUS_CONNECTION_STRING = "Endpoint=sb://...;SharedAccessKey=..."
> ```

## Step 5 — Run it

```bash
dotnet run
```
Expected output:
```
Sent 1 message to queue 'orders'.
```

## Step 6 — Verify the message landed

In the Azure Portal → namespace → `orders` queue:
- The **Active message count** (queue Overview) goes up by one.
- **Service Bus Explorer** → **Peek** shows your message body.

(Or run a receiver — the Functions `OrderCreatedFunction` in this repo, or a
`ServiceBusProcessor`/`ServiceBusReceiver` console — to consume it; see the SDK
types section in `docs/SERVICE_BUS.md`.)

## Sending many messages (optional)

For more than a couple messages, batch them — one network call instead of N:
```csharp
using ServiceBusMessageBatch batch = await sender.CreateMessageBatchAsync();
for (int i = 1; i <= 5; i++)
    batch.TryAddMessage(new ServiceBusMessage($"item {i}"));
await sender.SendMessagesAsync(batch);
```

## Troubleshooting

- **`UnauthorizedAccessException` / "Put token failed"** → wrong/truncated
  connection string, or the SAS policy lacks **Send** rights.
- **`MessagingEntityNotFoundException`** → the queue name doesn't exist in that
  namespace (typo, or wrong namespace).
- **Hangs then times out** → network/firewall blocking AMQP port 5671; set
  `new ServiceBusClient(cs, new ServiceBusClientOptions { TransportType = ServiceBusTransportType.AmqpWebSockets })`
  to fall back to WebSockets (port 443).

## What's real vs. reference-only in this repo

`samples/ServiceBusSenderConsole/` **builds cleanly** and matches the real SDK
usage exactly. It wasn't *run* here (no Azure Service Bus namespace available, and
the Smart App Control restriction noted in `docs/ARCHITECTURE.md`), but it runs
as-written with a real connection string + queue on a normal machine.

---

## Interview recap

**Q: What's the minimum code to send a Service Bus message?**
Create a `ServiceBusClient` from a connection string, get a `ServiceBusSender` for
the queue via `CreateSender(queueName)`, build a `ServiceBusMessage`, and call
`SendMessageAsync`. One NuGet package (`Azure.Messaging.ServiceBus`) covers it.

**Q: Client vs. sender — what's the difference and how many of each?**
The client owns the connection and is expensive, so you keep one (a singleton in a
real app). The sender is a cheap, queue-specific object created from the client;
you can cache one per queue. Creating a new client per message is the common
anti-pattern to avoid.
