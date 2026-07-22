using Azure.Messaging.ServiceBus;

// ---------------------------------------------------------------------------
// Minimal Service Bus SENDER console app.
// The smallest possible "send one message to a queue" program — no DI, no web
// host, just the SDK. See docs/SERVICE_BUS_SENDER_CONSOLE.md for the full
// step-by-step (where to copy the connection string and queue name from).
// ---------------------------------------------------------------------------

// 1) The connection string and queue name.
//    Copy the CONNECTION STRING from the Azure portal:
//      namespace (or queue) -> Shared access policies -> RootManageSharedAccessKey
//      -> Primary Connection String.
//    Copy the QUEUE NAME from the namespace's Queues list (this repo uses "orders").
//
//    SECURITY: never commit a real connection string. This reads it from an
//    environment variable so the secret stays off disk. Set it first:
//      Windows (PowerShell):  $env:SERVICE_BUS_CONNECTION_STRING = "Endpoint=sb://..."
//      then run:              dotnet run
string connectionString =
    Environment.GetEnvironmentVariable("SERVICE_BUS_CONNECTION_STRING")
    ?? "Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>";

string queueName = "orders";

if (connectionString.Contains("<your-namespace>"))
{
    Console.WriteLine("Set SERVICE_BUS_CONNECTION_STRING to your real connection string first. See docs/SERVICE_BUS_SENDER_CONSOLE.md.");
    return;
}

// 2) Create the client (owns the connection) and a sender for the queue.
//    In a real app the client is a singleton; in a tiny console app we just
//    create one, use it, and dispose it. `await using` disposes both cleanly.
await using ServiceBusClient client = new ServiceBusClient(connectionString);
ServiceBusSender sender = client.CreateSender(queueName);

// 3) Build a message and send it.
string body = $"Hello from the console sender at {DateTime.UtcNow:O}";
ServiceBusMessage message = new ServiceBusMessage(body)
{
    ContentType = "text/plain",
    MessageId = Guid.NewGuid().ToString()
};

await sender.SendMessageAsync(message);

Console.WriteLine($"Sent 1 message to queue '{queueName}':");
Console.WriteLine($"  {body}");

// (Optional) send a small batch — the efficient way to send many at once:
// using ServiceBusMessageBatch batch = await sender.CreateMessageBatchAsync();
// for (int i = 1; i <= 5; i++) batch.TryAddMessage(new ServiceBusMessage($"batch item {i}"));
// await sender.SendMessagesAsync(batch);
