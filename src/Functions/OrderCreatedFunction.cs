using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Functions;

/// <summary>
/// Mirrors WebApp's Configuration/OrderCreatedMessage.cs. Deliberately
/// duplicated rather than shared via a common project reference — WebApp and
/// Functions are two independently deployable Azure resources, the same way
/// HighFidelity-Api and HighFidelity-Ui don't share a project either. The
/// wire contract (JSON shape) is what actually needs to match, not the type.
/// </summary>
public record OrderCreatedMessage(int OrderId, string Customer, decimal Amount, DateTime CreatedAtUtc);

/// <summary>
/// Event-driven trigger: runs when a message lands in the "orders" Service
/// Bus queue — this is the "event" trigger type, and also the receiver half
/// of the publisher/receiver pair (WebApp.Controllers.OrdersController is
/// the publisher). See docs/FUNCTION_APPS.md and docs/SERVICE_BUS.md.
/// </summary>
public class OrderCreatedFunction
{
    private readonly ILogger<OrderCreatedFunction> _logger;

    public OrderCreatedFunction(ILogger<OrderCreatedFunction> logger) => _logger = logger;

    [Function("OrderCreatedFunction")]
    public void Run(
        [ServiceBusTrigger("orders", Connection = "ServiceBusConnection")] string messageBody)
    {
        var order = JsonSerializer.Deserialize<OrderCreatedMessage>(messageBody)
            ?? throw new InvalidOperationException("Received an order message that didn't deserialize.");

        // A real implementation would do something idempotent here (write to
        // a database, call another API) — logging is the stand-in so the
        // trigger's shape stays the focus. The Functions runtime completes
        // (removes) the message automatically when this method returns
        // without throwing; throwing leaves it for the queue's retry policy,
        // then the dead-letter subqueue after MaxDeliveryCount is exceeded.
        _logger.LogInformation(
            "Processing order {OrderId} for {Customer}, amount {Amount:C}, created {CreatedAtUtc}",
            order.OrderId, order.Customer, order.Amount, order.CreatedAtUtc);
    }
}
