using Azure.Messaging.ServiceBus;

// ---------------------------------------------------------------------------
// Dead-Letter Queue (DLQ) demo.
// Shows the two DLQ operations people ask about:
//   PART A - SEND a message to the dead-letter queue (dead-letter it explicitly)
//   PART B - READ messages back FROM the dead-letter queue
// Run the sender sample first to put a message on the main 'orders' queue.
// See docs/SERVICE_BUS_DEAD_LETTER_QUEUE.md for the full explanation.
// ---------------------------------------------------------------------------

string connectionString =
    Environment.GetEnvironmentVariable("SERVICE_BUS_CONNECTION_STRING")
    ?? "Endpoint=sb://<your-namespace>.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>";

string queueName = "orders";

if (connectionString.Contains("<your-namespace>"))
{
    Console.WriteLine("Set SERVICE_BUS_CONNECTION_STRING first. See docs/SERVICE_BUS_DEAD_LETTER_QUEUE.md.");
    return;
}

await using ServiceBusClient client = new ServiceBusClient(connectionString);

// ---------------------------------------------------------------------------
// PART A — Send a message to the dead-letter queue.
// You don't create a DLQ message directly; you RECEIVE a normal message and
// "dead-letter" it — Service Bus moves it into that queue's DLQ sub-queue.
// Here we receive one from the main queue and dead-letter it with a reason.
// ---------------------------------------------------------------------------
ServiceBusReceiver receiver = client.CreateReceiver(queueName);

ServiceBusReceivedMessage? message =
    await receiver.ReceiveMessageAsync(maxWaitTime: TimeSpan.FromSeconds(5));

if (message is not null)
{
    Console.WriteLine($"Received from main queue: {message.Body}");

    // Move it to the DLQ, tagging WHY (these become DeadLetterReason /
    // DeadLetterErrorDescription on the dead-lettered message).
    await receiver.DeadLetterMessageAsync(
        message,
        deadLetterReason: "ValidationFailed",
        deadLetterErrorDescription: "Order amount was negative — cannot process.");

    Console.WriteLine("  -> dead-lettered (moved to the DLQ).");
}
else
{
    Console.WriteLine("Main queue empty — run the sender sample first to have something to dead-letter.");
}

// ---------------------------------------------------------------------------
// PART B — Read messages back from the dead-letter queue.
// The DLQ is a real sub-queue of the same queue. You open a receiver on it by
// setting SubQueue = SubQueue.DeadLetter (no separate queue name).
// ---------------------------------------------------------------------------
ServiceBusReceiver dlqReceiver = client.CreateReceiver(
    queueName,
    new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

Console.WriteLine("\nReading the dead-letter queue:");

while (true)
{
    ServiceBusReceivedMessage? dead =
        await dlqReceiver.ReceiveMessageAsync(maxWaitTime: TimeSpan.FromSeconds(5));

    if (dead is null)
    {
        Console.WriteLine("DLQ empty. Done.");
        break;
    }

    // The reason/description we set when dead-lettering are readable here —
    // this is how you triage WHY each message failed.
    Console.WriteLine($"  DLQ message: {dead.Body}");
    Console.WriteLine($"    Reason:      {dead.DeadLetterReason}");
    Console.WriteLine($"    Description: {dead.DeadLetterErrorDescription}");

    // Completing here removes it from the DLQ. In real triage you'd inspect,
    // fix the cause, optionally re-publish a corrected message to the main
    // queue, then complete the dead-letter copy to clear it.
    await dlqReceiver.CompleteMessageAsync(dead);
    Console.WriteLine("    -> completed (removed from DLQ).");
}
