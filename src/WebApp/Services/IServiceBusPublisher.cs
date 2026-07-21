namespace WebApp.Services;

public interface IServiceBusPublisher
{
    Task PublishAsync<T>(string queueName, T message);
}
