# Resilience — Retries, Circuit Breakers, Timeouts (Polly)

## The plain-English version

Every call your app makes to something else over a network — a downstream API, a database, a payment gateway — can fail for reasons that have nothing to do with your code being wrong. The network blips. The other service is briefly overloaded. A load balancer is mid-deploy. **Resilience** is the discipline of handling those failures on purpose instead of letting them crash straight through to your user as a 500.

Three ideas cover almost everything asked about this in interviews:

- **Retry** — if a call fails in a way that's likely *transient* (a 503, a timeout, a dropped connection), try again a few times before giving up, usually waiting a little longer between each attempt ("backoff"), with some randomness added ("jitter") so a thousand clients don't all retry at the exact same millisecond and hammer the recovering service in unison.
- **Circuit breaker** — if a downstream dependency is failing *persistently*, not transiently, stop calling it for a while. Keep hammering a service that's already on fire and you become part of the problem — you queue up threads waiting on a service that isn't coming back soon, and you never let it recover. A circuit breaker "trips" (opens) after enough consecutive/recent failures, fails fast for a cooldown period without even attempting the call, then lets a trial request through to see if things have recovered ("half-open") before fully closing again.
- **Timeout** — don't wait forever for a response. A call that takes 90 seconds to fail is worse than one that fails in 2, because it ties up a thread/connection the whole time.

**Polly** is the .NET library that implements all three (plus more: bulkhead isolation, hedging, fallback). It's been the standard for years; as of .NET 8, Microsoft ships a thin wrapper around Polly v8 — `Microsoft.Extensions.Http.Resilience` — that wires a sensible default combination of retry + circuit breaker + timeout onto an `HttpClient` in a single line.

## The analogy that makes it click

Think of calling a busy restaurant that isn't picking up:
- **Retry** = you call again a couple of times, waiting a bit longer each time, assuming it's just a busy phone line.
- **Circuit breaker** = after the fifth unanswered call in a row, you stop dialing for 30 seconds and just assume they're slammed — no point wearing out your finger. After the cooldown, you try once more before deciding to keep calling normally again.
- **Timeout** = you don't stay on hold for 20 minutes hoping someone picks up; you hang up after a reasonable wait and try something else.

## How this repo implements it

**`src/WebApp/Program.cs`** registers a named `HttpClient` and attaches the standard resilience handler in one call:

```csharp
builder.Services.AddHttpClient("ResilientClient", client =>
{
    var baseUrl = builder.Configuration["ResilienceDemo:TargetBaseUrl"] ?? "http://localhost:5175";
    client.BaseAddress = new Uri(baseUrl);
})
.AddStandardResilienceHandler();
```

`AddStandardResilienceHandler()` bolts on Polly v8's default pipeline: retry (exponential backoff + jitter, up to a handful of attempts) → circuit breaker → a per-attempt timeout → an overall total-request timeout. It treats HTTP 5xx, 408, and network-level exceptions as retryable by default — you don't have to hand-write the "is this transient?" predicate yourself.

**`src/WebApp/Controllers/ResilienceController.cs`** exists purely to make the pipeline observable without needing a real flaky third-party API:
- `GET /api/resilience/flaky-target` — simulates an unreliable dependency: fails 2 out of every 3 calls with `503`, or fails *every* call if "always-fail" mode is switched on.
- `POST /api/resilience/flaky-target/reset` — resets the counter.
- `POST /api/resilience/flaky-target/always-fail/{enabled}` — toggles permanent failure, to demo the circuit breaker instead of just retries.
- `GET /api/resilience/call-flaky` — calls `flaky-target` *through* the `ResilientClient` (i.e., through the whole Polly pipeline). With default mod-3 flakiness this almost always comes back `200 OK`, because retries silently absorbed the `503`s underneath.
- `GET /api/resilience/hammer-flaky/{times}` — fires many calls back-to-back and reports the outcome of each, so you can watch retries recovering transient failures, and — with always-fail switched on — watch later calls come back instantly as a `BrokenCircuitException` instead of a real `503`, because the circuit opened and stopped even attempting the call.

## What's real vs. reference-only in this repo

The code compiles cleanly and the pipeline registration is correct, matching the real `Microsoft.Extensions.Http.Resilience` API surface. **It could not be exercised at runtime in this environment** — mid-session, this machine's Windows **Smart App Control** policy started blocking execution of freshly-built, unsigned local binaries (confirmed via the CodeIntegrity event log: `WebApp.exe` failed to load `WebApp.dll`, "did not meet the Enterprise signing level requirements"). This is a machine security policy, not a code issue, and it now blocks `dotnet run` for this repo entirely (it affected the Functions project identically — see `docs/DURABLE_FUNCTIONS.md`). If you're reading this after resolving that restriction (or on a different machine), running `dotnet run` in `src/WebApp` and hitting `/api/resilience/hammer-flaky/10` a few times, then toggling always-fail and hammering again, is exactly how to verify it for real.

---

## Interview Questions

**Q: What's the difference between a retry and a circuit breaker, and why do you need both?**
Retry handles *transient* failures — a single blip you expect to recover from on the next attempt. A circuit breaker handles *sustained* failure — the dependency is genuinely down, and retrying just adds load to something already struggling and wastes your own threads waiting on calls that won't succeed. Retry without a circuit breaker means you keep hammering a dead service forever, one request at a time, each one still doing the full retry sequence.

**Q: Why does retry backoff need jitter, not just an increasing delay?**
Without jitter, every client that started failing at the same moment retries at exactly the same intervals — you get synchronized waves of load hitting the recovering service right as it comes back up (the "thundering herd" problem), which can knock it back down. Jitter randomizes the delay slightly so retries spread out over time instead of arriving in a synchronized burst.

**Q: What are the states of a circuit breaker?**
Closed (normal — calls go through, failures are counted), Open (tripped — calls fail immediately without being attempted, for a cooldown period), and Half-Open (after cooldown, a limited number of trial calls are let through; if they succeed the circuit closes again, if they fail it reopens).

**Q: Should you retry every kind of failure?**
No — only ones plausibly transient. Retrying a `400 Bad Request` or a `401 Unauthorized` is pointless; the request will fail identically every time because the problem is the request itself, not a temporary condition on the server. You retry `503`, `408`, connection resets, timeouts — not client errors.

**Q: Where does Polly actually sit in the request pipeline?**
It wraps the `HttpClient`'s `DelegatingHandler` chain — the same mechanism used for things like auth token injection (see the `AuthTokenHandler` bug in the sibling `HighFidelity-Ui` project, where a `DelegatingHandler`'s `InnerHandler` wasn't wired up and every call silently failed). `AddStandardResilienceHandler()` inserts Polly's handler into that chain via `IHttpClientFactory`, so every call made through that named client automatically goes through retry/circuit-breaker/timeout without the calling code knowing or caring.

**Q: What's the cost of adding resilience policies — is there a downside?**
Retries multiply load on a struggling dependency (bounded by attempt count, but still real) and increase tail latency for calls that do eventually fail all attempts. Circuit breakers can produce false trips under normal traffic spikes if thresholds are tuned too aggressively, causing you to reject healthy traffic. Resilience isn't free — it trades a bit of complexity and some worst-case latency for much better behavior under partial failure.
