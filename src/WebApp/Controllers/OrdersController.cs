using Microsoft.AspNetCore.Mvc;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Controllers;

/// <summary>
/// Publisher side of the Service Bus demo — "creating an order" here just
/// means publishing an event; nothing is persisted. The Functions project's
/// OrderCreatedFunction (Service Bus trigger) is the receiver. See
/// docs/SERVICE_BUS.md.
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private const string QueueName = "orders";
    private readonly IServiceBusPublisher _publisher;

    public OrdersController(IServiceBusPublisher publisher) => _publisher = publisher;

    public record CreateOrderRequest(string Customer, decimal Amount);

    [HttpPost]
    public async Task<ActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var message = new OrderCreatedMessage(
            OrderId: Random.Shared.Next(10000, 99999),
            Customer: request.Customer,
            Amount: request.Amount,
            CreatedAtUtc: DateTime.UtcNow);

        await _publisher.PublishAsync(QueueName, message);

        return Accepted(new { message.OrderId, Status = "Published to Service Bus, awaiting processing" });
    }
}
