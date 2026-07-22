using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace WebApp.Controllers;

// Demonstrates the one Cosmos DB concept that comes up in almost every
// interview about it: partition keys, and why a point read (partition key +
// id) is cheap while a cross-partition query is expensive. Only usable when
// Cosmos:ConnectionString is configured (a real account or the Cosmos DB
// Emulator) — see docs/SQL_VS_COSMOS.md for what's actually verified here.
[ApiController]
[Route("api/cosmos-demo")]
public class CosmosDemoController : ControllerBase
{
    private const string DatabaseId = "HighFidelityDb";
    private const string ContainerId = "Orders";

    private readonly CosmosClient? _cosmosClient;

    public CosmosDemoController(IServiceProvider services)
    {
        // Resolved manually rather than constructor-injected, because
        // CosmosClient is only registered when Cosmos:ConnectionString is
        // set — this controller still needs to exist (and compile, and
        // return a clear error) even when it isn't.
        _cosmosClient = services.GetService(typeof(CosmosClient)) as CosmosClient;
    }

    // A point read: partition key + id together locate the item directly,
    // without scanning. This is the cheap, fast path Cosmos is built around.
    [HttpGet("orders/{customerId}/{orderId}")]
    public async Task<IActionResult> GetOrder(string customerId, string orderId)
    {
        if (_cosmosClient is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Cosmos:ConnectionString is not configured — see docs/SQL_VS_COSMOS.md." });
        }

        var container = _cosmosClient.GetContainer(DatabaseId, ContainerId);
        try
        {
            var response = await container.ReadItemAsync<OrderDocument>(orderId, new PartitionKey(customerId));
            return Ok(new { requestChargeRUs = response.RequestCharge, order = response.Resource });
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return NotFound();
        }
    }

    // A cross-partition query: no partition key given, so Cosmos has to fan
    // the query out to every physical partition and merge results — this is
    // the expensive path, and the RU charge reported back makes that
    // difference visible rather than theoretical.
    [HttpGet("orders/by-status/{status}")]
    public async Task<IActionResult> GetOrdersByStatus(string status)
    {
        if (_cosmosClient is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Cosmos:ConnectionString is not configured — see docs/SQL_VS_COSMOS.md." });
        }

        var container = _cosmosClient.GetContainer(DatabaseId, ContainerId);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.status = @status")
            .WithParameter("@status", status);

        var results = new List<OrderDocument>();
        double totalCharge = 0;
        using var iterator = container.GetItemQueryIterator<OrderDocument>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            totalCharge += page.RequestCharge;
            results.AddRange(page);
        }

        return Ok(new { requestChargeRUs = totalCharge, count = results.Count, orders = results });
    }

    private record OrderDocument(string id, string customerId, string status, decimal total);
}
