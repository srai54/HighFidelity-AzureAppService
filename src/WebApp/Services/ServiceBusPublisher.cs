using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace WebApp.Services;

/// <summary>
/// Publisher side of the queue. <see cref="ServiceBusClient"/> is expensive to
/// construct and safe to share, so it's a singleton (see Program.cs); senders
/// are cheap and cached per queue name rather than one-per-call.
/// See docs/SERVICE_BUS.md for the receiver side (the Functions project).
/// </summary>
public class ServiceBusPublisher : IServiceBusPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusPublisher(ServiceBusClient client) => _client = client;

    public async Task PublishAsync<T>(string queueName, T message)
    {
        var sender = _senders.GetOrAdd(queueName, _client.CreateSender);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var serviceBusMessage = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString()
        };

        await sender.SendMessageAsync(serviceBusMessage);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
            await sender.DisposeAsync();
    }
}
