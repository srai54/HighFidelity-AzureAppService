using Azure.Messaging.ServiceBus;

// ---------------------------------------------------------------------------
// Minimal Service Bus RECEIVER console app (manual pull with ServiceBusReceiver).
// The companion to samples/ServiceBusSenderConsole — run the sender to put
// messages on the queue, then run this to pull them off. See
// docs/SERVICE_BUS_RECEIVER_CONSOLE.md for the full step-by-step.
// ---------------------------------------------------------------------------

// 1) Connection string + queue name — same two values as the sender.
//    Copy the connection string from the portal:
//      namespace/queue -> Shared access policies -> RootManageSharedAccessKey
//      -> Primary Connection String (Listen rights are enough for a receiver).
//    Read from an environment variable so no real secret is committed:
//      $env:SERVICE_BUS_CONNECTION_STRING = "Endpoint=sb://..."
string connectionString =
    Environment.GetEnvironmentVariable("SERVICE_BUS_CONNECTION_STRING")
    ?? "Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>";

string queueName = "orders";

if (connectionString.Contains("<your-namespace>"))
{
    Console.WriteLine("Set SERVICE_BUS_CONNECTION_STRING to your real connection string first. See docs/SERVICE_BUS_RECEIVER_CONSOLE.md.");
    return;
}

// 2) Create the client (owns the connection) and a RECEIVER for the queue.
//    CreateReceiver takes the queue name. Default receive mode is PeekLock:
//    a received message is locked (invisible to others) until you complete it.
await using ServiceBusClient client = new ServiceBusClient(connectionString);
ServiceBusReceiver receiver = client.CreateReceiver(queueName);

Console.WriteLine($"Receiving from queue '{queueName}' (Ctrl+C to stop)...");

// 3) Pull messages in a loop. ReceiveMessageAsync waits up to maxWaitTime for a
//    message; it returns null when nothing arrives in that window, which we use
//    as the signal to stop. (A long-running service would loop forever instead.)
while (true)
{
    ServiceBusReceivedMessage? message =
        await receiver.ReceiveMessageAsync(maxWaitTime: TimeSpan.FromSeconds(5));

    if (message is null)
    {
        Console.WriteLine("No more messages. Done.");
        break;
    }

    // Read the body.
    Console.WriteLine($"Received: {message.Body}");

    try
    {
        // ... process the message here ...

        // Completing removes it from the queue for good.
        await receiver.CompleteMessageAsync(message);
        Console.WriteLine("  -> completed (removed from queue)");
    }
    catch (Exception ex)
    {
        // Four ways to settle a message (see docs/SERVICE_BUS_RECEIVER_CONSOLE.md):
        //   CompleteMessageAsync  - success, remove it (the happy path above)
        //   AbandonMessageAsync   - release lock -> redelivered (retry); this branch
        //   DeferMessageAsync     - set aside; retrieve later by sequence number
        //   DeadLetterMessageAsync- park in the dead-letter subqueue immediately
        Console.WriteLine($"  -> abandoning after error: {ex.Message}");
        await receiver.AbandonMessageAsync(message);
    }
}
