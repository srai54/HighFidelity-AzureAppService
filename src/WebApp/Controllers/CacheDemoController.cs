using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;

namespace WebApp.Controllers;

// Demonstrates the cache-aside pattern against IDistributedCache — the
// abstraction that Azure Cache for Redis normally sits behind in production.
// Program.cs registers AddStackExchangeRedisCache when Redis:ConnectionString
// is configured, or falls back to AddDistributedMemoryCache otherwise (this
// environment has no local Redis/Docker available). This controller's code
// is identical either way — that's the entire point of coding against
// IDistributedCache instead of a Redis-specific client. See
// docs/REDIS_CACHE.md.
[ApiController]
[Route("api/cache-demo")]
public class CacheDemoController : ControllerBase
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static int _expensiveCallCount;

    private readonly IDistributedCache _cache;

    public CacheDemoController(IDistributedCache cache)
    {
        _cache = cache;
    }

    // Cache-aside: check the cache first; on a miss, do the "expensive" work,
    // store the result with an expiry, then return it. A second request
    // within CacheDuration returns instantly from cache and expensiveCallCount
    // doesn't increment — that's the whole pattern, visible in one counter.
    [HttpGet("product/{id}")]
    public async Task<IActionResult> GetProduct(int id)
    {
        var cacheKey = $"product:{id}";
        var cached = await _cache.GetStringAsync(cacheKey);

        if (cached is not null)
        {
            var cachedProduct = JsonSerializer.Deserialize<ProductDto>(cached);
            return Ok(new { source = "cache", product = cachedProduct });
        }

        var product = await LoadProductFromSourceOfTruthAsync(id);

        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(product),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration });

        return Ok(new { source = "source-of-truth", product });
    }

    // Cache invalidation — the other half of cache-aside. Call this after a
    // write to the real data store so the next read doesn't serve stale data.
    [HttpPost("product/{id}/invalidate")]
    public async Task<IActionResult> InvalidateProduct(int id)
    {
        await _cache.RemoveAsync($"product:{id}");
        return Ok(new { invalidated = $"product:{id}" });
    }

    [HttpPost("reset-counter")]
    public IActionResult ResetCounter()
    {
        Interlocked.Exchange(ref _expensiveCallCount, 0);
        return Ok(new { reset = true });
    }

    // Stands in for a slow database query / downstream API call — the thing
    // caching exists to avoid repeating.
    private static async Task<ProductDto> LoadProductFromSourceOfTruthAsync(int id)
    {
        Interlocked.Increment(ref _expensiveCallCount);
        await Task.Delay(300);
        return new ProductDto(id, $"Product {id}", _expensiveCallCount);
    }

    private record ProductDto(int Id, string Name, int LoadedByCallNumber);
}
