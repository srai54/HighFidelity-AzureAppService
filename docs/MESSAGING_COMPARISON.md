# Messaging Comparison — Service Bus vs. Event Grid vs. Storage Queues vs. Event Hub

## The plain-English version

This is the single most common "which one would you use" interview question in the whole messaging space, because Azure has four different answers that all sound similar ("it's a queue/messaging thing") but solve genuinely different problems. The fast way to tell them apart is to ask: **is this a command, a notification, a cheap simple job queue, or a firehose of events?**

- **Storage Queues** — the simplest possible queue. Cheap, basic FIFO-ish job queue, minimal features (no sessions, no dead-lettering as a first-class concept, 64KB message size cap). Use it when you just need "put a simple job in a queue, something will pick it up eventually" and don't need anything fancier. This repo doesn't implement it (Service Bus already covers "a queue" more thoroughly, and Storage Queues is the same shape with fewer features), but it's a real distinct product worth naming when this question comes up.
- **Service Bus** — a **message queue** built for reliable, ordered, enterprise messaging between specific producers and consumers: think **commands** ("process this order") and **point-to-point or topic/subscription work distribution**. Has sessions (ordered message groups), dead-lettering, duplicate detection, transactions. This is what's actually implemented in this repo — see `docs/SERVICE_BUS.md`.
- **Event Grid** — built for **discrete, low-volume, reactive notifications**: "a blob was uploaded," "a resource was created," "this specific thing just happened." Push-based (near-instant delivery to subscribers), designed to fan a single event out to many different subscribers (a function, a webhook, a logic app) that each react independently. Think of it as Azure's own pub/sub nervous system for "stuff happened" events across its own services.
- **Event Hub** — built for **high-throughput event streaming**: telemetry, logs, IoT sensor data, clickstreams — millions of events per second, consumers reading at their own pace from a durable, replayable log (via partitions and consumer groups, conceptually close to Kafka — because Event Hub literally speaks the Kafka protocol as one of its interfaces). It's not about a single important business event; it's about a continuous flood of events where you care about throughput and replayability more than any one message's individual handling.

The four-way split interviewers actually want to hear:

| | Service Bus | Event Grid | Storage Queues | Event Hub |
|---|---|---|---|---|
| **Shape** | Message queue (command/work item) | Event notification (reactive) | Simple job queue | Event stream (telemetry/log) |
| **Volume** | Moderate | Low-to-moderate, discrete events | Low-to-moderate | Very high (millions/sec) |
| **Delivery model** | Pull (consumer reads) | Push (near-instant to subscribers) | Pull | Pull, from a partitioned log |
| **Ordering** | Yes, via sessions | No guarantee | No guarantee | Yes, within a partition |
| **Replay old messages** | No (once consumed/expired, gone) | No | No | Yes — it's a log, consumers can rewind |
| **Typical use** | "Process this order" | "A blob was uploaded, notify subscribers" | "Queue a simple background job" | "Ingest a stream of IoT sensor readings" |

## The analogy that makes it click

- **Service Bus** is a **ticket queue at a specific service counter** — each ticket (message) is a specific task for a specific worker to handle, in order, with a documented process for what happens if a ticket can't be resolved (dead-lettering).
- **Event Grid** is a **PA system announcement** — "attention, the 3pm delivery has arrived" — broadcast once, and everyone who's subscribed to that kind of announcement reacts immediately, independently, in whatever way is relevant to them. Nobody "processes" the announcement as a task; they just react to having heard it.
- **Storage Queues** is a **suggestion box that gets emptied in rough order** — dead simple, no frills, fine for low-stakes background jobs.
- **Event Hub** is a **security camera's continuous recording** — a constant stream, not a single alert about a single event, where the point is capturing and later being able to review/rewind a high-volume feed, not handling any one frame as an individual task.

## How this repo covers it

Only **Service Bus** is actually implemented (publisher in `src/WebApp/Services/ServiceBusPublisher.cs` + `OrdersController`, receiver in `src/Functions/OrderCreatedFunction.cs` — see `docs/SERVICE_BUS.md` for the full picture, including what's verified). Event Grid, Storage Queues, and Event Hub are conceptual-only here — this doc exists specifically to make sure the *comparison* is covered even though only one of the four has working code, because "which one would you use and why" is asked at least as often as "how do you implement Service Bus."

## What's real vs. reference-only in this repo

This entire document is conceptual — no code for Event Grid, Storage Queues, or Event Hub exists in this repo. Service Bus (referenced throughout for contrast) has real, partially-verified code — see `docs/SERVICE_BUS.md` for its specific verification status.

---

## Interview Questions

**Q: Someone uploads a blob and three different downstream systems need to react to it — which service?**
Event Grid. It's built exactly for "something happened once, fan it out to multiple independent subscribers" — Blob Storage can emit a native Event Grid event on upload, and each subscriber (a Function, a webhook, another service) reacts on its own without any of them needing to know about the others.

**Q: You need to process orders reliably, one at a time, in the order they were placed for a given customer — which service?**
Service Bus, using sessions (a session ID groups messages — e.g., per customer — and Service Bus guarantees in-order delivery within a session). This is a command/work-item shape with a strict ordering requirement, which is exactly Service Bus's niche.

**Q: You're ingesting sensor readings from 50,000 IoT devices, tens of thousands of events per second — which service?**
Event Hub. The volume alone rules out Service Bus and Event Grid (neither is built for that throughput), and Event Hub's partitioned-log model is specifically designed for high-volume streaming ingestion, with consumers able to process at their own pace and even replay from an earlier point in the stream.

**Q: What's the single biggest structural difference between Event Hub and Service Bus?**
Event Hub is a durable, replayable log partitioned for high-throughput streaming — a consumer can rewind and re-read from an earlier offset. Service Bus is a queue — once a message is consumed (completed), it's gone; there's no "replay from yesterday" concept. That log-vs-queue distinction is also why Event Hub scales to much higher throughput: it doesn't track per-consumer delivery state the way a queue does.

**Q: Why would you ever pick Storage Queues over Service Bus, given Service Bus has more features?**
Cost and simplicity, when you don't need those extra features. Storage Queues is cheaper and simpler for a basic "just queue this job" use case — if you don't need sessions, dead-lettering, duplicate detection, or transactions, paying for and configuring Service Bus's extra capability is unnecessary overhead.

**Q: Is Event Grid push or pull, and why does that matter?**
Push — Event Grid delivers events to subscriber endpoints (an HTTP webhook, an Event Hub, a Function via its trigger) near-instantly as they occur, rather than subscribers polling for new events. This matters because it's built for low-latency reactive notification ("react to this the moment it happens"), not for a consumer that wants to process at its own pace, which is more the Service Bus/Event Hub pull model.
