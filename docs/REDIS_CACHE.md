# Azure Cache for Redis — Cache-Aside and IDistributedCache

## The plain-English version

Databases (and downstream APIs) are slow relative to memory. If the same piece of data gets read over and over — a product catalog entry, a user's session, a computed leaderboard — recomputing or re-querying it every single time wastes latency and load for no reason, when the value hasn't changed. **Redis** is an in-memory key-value store, and **Azure Cache for Redis** is Azure's managed version of it: extremely fast reads/writes, because it's RAM-backed, sitting in front of (not instead of) your real database.

The pattern almost every interview question about caching is really asking about is **cache-aside** (also called lazy loading):
1. Ask the cache first.
2. Cache hit → return it, done, never touched the real database.
3. Cache miss → go to the real database, get the value, **write it into the cache** (usually with an expiry), then return it.
4. On a write/update to the real data, **invalidate** (delete) the cached entry so the next read doesn't serve something stale.

The subtlety interviewers probe for: caching isn't free correctness-wise. You've now got two copies of the truth (the database and the cache), and the entire discipline of caching is managing how those two copies can drift apart (staleness) and for how long you're willing to tolerate that (the expiry/TTL).

In .NET, you don't usually code directly against a Redis client for this — you code against **`IDistributedCache`**, an abstraction with `GetAsync`/`SetAsync`/`RemoveAsync`. Redis is one implementation of it; so is "just keep it in this process's memory" (`AddDistributedMemoryCache`, non-shared, resets on restart, fine for local dev/single-instance testing). Same application code, different backing store — you swap the DI registration, not the controller.

## The analogy that makes it click

Cache-aside is exactly how you'd handle a frequently-asked question at a help desk. The first person to ask "what are your opening hours?" makes you go check the actual posted schedule (the database) — but you jot the answer on a sticky note (the cache) before answering. The next ten people who ask the same question get the sticky note instantly — you never re-check the posted schedule for them. If the schedule changes, you throw away the sticky note (invalidate) so the next person forces a fresh check instead of getting told outdated hours.

## How this repo implements it

**`src/WebApp/Program.cs`**:
```csharp
var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "HighFidelity:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
```
No local Redis is available in this environment (no Docker, no local Redis binary installed) — so, unlike Blob Storage's Azurite, there's no real Redis-compatible server to point at locally. The fallback to `AddDistributedMemoryCache()` isn't a workaround invented for this repo, though — it's a real, supported `IDistributedCache` implementation, which is exactly what makes this topic honestly demonstrable anyway: the interface is what matters, and application code genuinely cannot tell which implementation is behind it.

**`src/WebApp/Controllers/CacheDemoController.cs`** implements cache-aside end-to-end against `IDistributedCache`:
- `GET /api/cache-demo/product/{id}` — checks the cache first; on a miss, calls a simulated slow "source of truth" (a 300ms delay + an incrementing call counter), caches the result for 30 seconds, and returns it tagged with `source: "cache"` or `source: "source-of-truth"` so you can see exactly which path was taken.
- `POST /api/cache-demo/product/{id}/invalidate` — removes the cached entry, the other half of the pattern (call this after a write to the real store).
- `POST /api/cache-demo/reset-counter` — resets the "expensive call" counter for a clean demo run.

Calling `GET /api/cache-demo/product/1` twice in a row: the first response says `source-of-truth` with `loadedByCallNumber: 1`; the second, within 30 seconds, says `cache` and the underlying counter never incremented — that's cache-aside actually working, backed by the in-memory `IDistributedCache` implementation.

## What's real vs. reference-only in this repo

**The cache-aside pattern and `IDistributedCache` usage genuinely work** — `AddDistributedMemoryCache()` is a real, non-mocked implementation of the same interface Redis uses, so the get/miss/set/invalidate logic in `CacheDemoController` is exercised for real, just against an in-process store instead of a shared Redis instance. **What's specifically unverified** is `AddStackExchangeRedisCache` actually talking to a real (or emulated) Redis server — no local Redis was available (would need Docker or a Windows Redis build, neither installed here), and this session's separate Smart App Control restriction (see `docs/ARCHITECTURE.md`) blocked running the app locally at all regardless. The registration code matches the real `Microsoft.Extensions.Caching.StackExchangeRedis` API surface.

---

## Interview Questions

**Q: Walk through cache-aside step by step.**
Check the cache. Hit → return the cached value, done. Miss → query the real data source, store what you got into the cache with an expiry, then return it. On any write to the real data source, delete (invalidate) the corresponding cache entry so future reads don't see stale data until it's naturally re-populated on the next miss.

**Q: What's the risk with caching, and how do you manage it?**
Staleness — the cache can hold a value that no longer matches the real source of truth, for up to however long the expiry/TTL is (or until an explicit invalidation happens, if you remember to call it everywhere the data changes). You manage it by choosing a TTL appropriate to how tolerant the data is of being briefly stale (a product description can tolerate minutes of staleness; an account balance usually can't), and by invalidating explicitly on writes rather than relying on TTL alone wherever correctness actually matters.

**Q: Why code against `IDistributedCache` instead of a Redis client directly?**
Decoupling — the application logic (check cache, handle miss, populate cache) doesn't need to know or care what's behind `IDistributedCache`. You can run `AddDistributedMemoryCache()` locally/in tests and `AddStackExchangeRedisCache()` in Azure with zero changes to the controllers/services that actually use the cache — exactly like this repo does.

**Q: What happens if two requests get a cache miss for the same key at the same time?**
Both proceed to the source of truth and both write the same value back to the cache (a harmless, if slightly wasteful, double-computation) — this is the "cache stampede" / "thundering herd" edge case. For expensive-enough operations under real concurrency, you'd add a lock or a request-coalescing mechanism so only one caller actually does the work and the rest wait on that result — not implemented in this simple demo, but worth naming if asked.

**Q: When would you NOT reach for a distributed cache like Redis?**
When the data changes so frequently that the cache would almost never actually be hit before the next write invalidates it (the overhead of managing the cache outweighs the benefit), or when strict, immediate consistency is required and any staleness window — even milliseconds — is unacceptable (e.g., certain financial balance checks), or when the dataset is small/cheap enough that the source query is already fast and caching adds complexity for no measurable win.

**Q: What's the difference between session state, output caching, and a distributed cache like this?**
They can all be backed by the same Redis instance, but they solve different problems: session state stores per-user data across requests when your app runs on multiple instances (so a user isn't pinned to whichever server first served them — "sticky sessions" avoided); output caching stores entire rendered HTTP responses keyed by request; a general distributed cache (what this repo demos) stores arbitrary application data (a product, a computed result) that your own code explicitly reads and writes.
