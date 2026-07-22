using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace Functions;

// Durable Functions example: an HTTP call starts an orchestration that
// validates a batch of orders in parallel (fan-out/fan-in), then runs one
// final activity once they've all finished (chaining). See
// docs/DURABLE_FUNCTIONS.md for the theory and interview questions.
//
// The three function "roles" that make up every Durable Functions app:
//   1. Client (StartOrderBatch)       - starts an orchestration instance, returns a status-check URL
//   2. Orchestrator (OrderBatchOrchestrator) - describes the workflow; must be deterministic, no I/O of its own
//   3. Activities (ValidateOrderActivity, FinalizeBatchActivity) - do the actual work (I/O allowed here)
public class OrderBatchOrchestration
{
    private readonly ILogger<OrderBatchOrchestration> _logger;

    public OrderBatchOrchestration(ILogger<OrderBatchOrchestration> logger)
    {
        _logger = logger;
    }

    // Client function — the only piece callable from outside. Kicks off the
    // orchestration and immediately returns 202 Accepted with a status URL,
    // rather than blocking on the whole batch (which could take a while).
    [Function(nameof(StartOrderBatch))]
    public async Task<HttpResponseData> StartOrderBatch(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "orders/batch")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        // A fixed sample batch, to keep this endpoint dependency-free — a
        // real caller would post the order IDs to validate in the request body.
        var orderIds = new[] { "ORD-1001", "ORD-1002", "ORD-1003", "ORD-1004" };

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(OrderBatchOrchestrator), orderIds);

        _logger.LogInformation("Started order batch orchestration {InstanceId} for {Count} orders", instanceId, orderIds.Length);

        return await client.CreateCheckStatusResponseAsync(req, instanceId);
    }

    // Orchestrator function — describes the workflow, doesn't do the work
    // itself. Must be deterministic: no direct I/O, no Guid.NewGuid(), no
    // DateTime.Now — everything that could vary on replay has to go through
    // the context (context.CurrentUtcDateTime, activities, etc.), because
    // the Durable Task framework replays this function's code from the top
    // every time it wakes back up after an await, reconstructing state from
    // its history rather than actually keeping the method on a real stack.
    [Function(nameof(OrderBatchOrchestrator))]
    public static async Task<object> OrderBatchOrchestrator(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var orderIds = context.GetInput<string[]>() ?? Array.Empty<string>();

        // Fan-out: schedule one activity call per order, all in parallel.
        var validationTasks = orderIds
            .Select(orderId => context.CallActivityAsync<bool>(nameof(ValidateOrderActivity), orderId))
            .ToList();

        // Fan-in: wait for every parallel activity to complete before moving on.
        var validationResults = await Task.WhenAll(validationTasks);

        var validCount = validationResults.Count(v => v);

        // Chaining: a final activity that only runs after the fan-in above
        // has fully completed, using its aggregated result as input.
        var summary = await context.CallActivityAsync<string>(
            nameof(FinalizeBatchActivity),
            new BatchResult(orderIds.Length, validCount));

        return new { orderIds.Length, validCount, summary };
    }

    // Activity function — where actual work/I/O happens. This one just
    // simulates a validation check (in a real system: call a database, an
    // inventory API, a fraud-check service).
    [Function(nameof(ValidateOrderActivity))]
    public static async Task<bool> ValidateOrderActivity(
        [ActivityTrigger] string orderId, FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(ValidateOrderActivity));
        logger.LogInformation("Validating {OrderId}", orderId);

        await Task.Delay(500); // stand-in for a real downstream call

        // Deterministic "validation" so results are reproducible: every
        // order ending in an odd digit passes.
        var lastDigit = orderId[^1];
        var isValid = (lastDigit - '0') % 2 == 1;
        return isValid;
    }

    [Function(nameof(FinalizeBatchActivity))]
    public static async Task<string> FinalizeBatchActivity(
        [ActivityTrigger] BatchResult result, FunctionContext executionContext)
    {
        var logger = executionContext.GetLogger(nameof(FinalizeBatchActivity));
        await Task.Delay(200);
        var summary = $"{result.ValidCount}/{result.Total} orders passed validation.";
        logger.LogInformation(summary);
        return summary;
    }
}

public record BatchResult(int Total, int ValidCount);
