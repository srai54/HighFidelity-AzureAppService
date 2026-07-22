# Azure SQL vs. Cosmos DB — Relational vs. NoSQL, and Why Partition Keys Matter

## The plain-English version

**Azure SQL Database** is a managed relational database (SQL Server under the hood) — tables, foreign keys, joins, transactions across multiple tables, strong schema. This is what `HighFidelity-Api` (the sibling backend repo) actually uses via EF Core, and it's the right default for most line-of-business data: orders, users, anything with clear relationships and where you want the database itself to enforce integrity (a foreign key that can't dangle, a transaction that can't half-commit).

**Cosmos DB** is Azure's globally-distributed NoSQL database. The two things that actually differentiate it in practice:
1. **No fixed schema, no joins** — data is stored as JSON-like documents (or key-value, graph, column-family, depending on which API you pick; the document/"Core (SQL) API" is by far the most common). Each document is self-contained; you generally denormalize (embed related data) rather than join across containers, because there's no cross-container join operation.
2. **Everything scales around the partition key.** Every container has one, chosen at creation and — like Blob types — not something you casually change later. Cosmos physically distributes your data across partitions based on this key's value. **A query that includes the partition key** goes straight to the one physical partition that holds the answer — fast, cheap. **A query that doesn't** has to fan out to *every* partition and merge the results — slow, and billed for it (Cosmos charges in Request Units, RUs, and a cross-partition query burns far more of them). Picking a good partition key — something with high cardinality that matches your most common query pattern — is the single most consequential Cosmos design decision, and it's exactly why interviewers ask about it specifically instead of just "what is Cosmos DB."

The real decision in practice isn't "NoSQL is more modern, pick that" — it's: do you need multi-table transactions, joins, and strict relational integrity (→ SQL)? Or do you need to scale horizontally to huge volumes/global distribution with flexible, evolving document shapes, and you're willing to design around a partition key and denormalization (→ Cosmos)?

## The analogy that makes it click

Azure SQL is a **filing cabinet with cross-referenced folders** — every folder can point to another (foreign keys), and you can pull related documents from multiple folders in one trip (a join), but the whole cabinet lives in one place and everyone's request goes through the same drawers.

Cosmos DB with a good partition key is a **chain of separate filing cabinets, one per region/customer/whatever you partitioned on**, each holding everything that person/thing needs *without* having to walk to another cabinet (because you denormalized, embedding what you'd otherwise have joined). Ask for something by knowing which cabinet it's in (the partition key) and you go straight there. Ask a question that could be filed in any of the cabinets ("find every folder mentioning X," no partition key given) and you have to walk to *every single cabinet* and check — that's the cross-partition query, and it's why it's slow.

## How this repo implements it

**`src/WebApp/Program.cs`** registers a `CosmosClient` conditionally — only when `Cosmos:ConnectionString` is set (a real account, or the Cosmos DB Emulator's well-known local connection string):
```csharp
var cosmosConnectionString = builder.Configuration["Cosmos:ConnectionString"];
if (!string.IsNullOrWhiteSpace(cosmosConnectionString))
{
    builder.Services.AddSingleton(new CosmosClient(cosmosConnectionString));
}
```

**`src/WebApp/Controllers/CosmosDemoController.cs`** deliberately implements both query shapes side by side, so the partition-key distinction is something you can literally see the cost of, not just read about:
- `GET /api/cosmos-demo/orders/{customerId}/{orderId}` — a **point read**: partition key (`customerId`) + id together, resolved with `container.ReadItemAsync`, going straight to one partition.
- `GET /api/cosmos-demo/orders/by-status/{status}` — a **cross-partition query**: no partition key in the filter, so Cosmos fans out across every partition. Both endpoints return the actual `requestChargeRUs` from the response so the cost difference between the two is a real number, not an assertion.

The container is modeled with `customerId` as the partition key (`OrderDocument(string id, string customerId, string status, decimal total)`) — a reasonable choice for a system where "get this customer's orders" is the dominant query pattern, matching how the sibling `HighFidelity-Api` backend's relational `Orders` table is actually queried today.

For comparison, `HighFidelity-Api`'s existing relational schema (`database/seed.sql`, EF Core models) is the "when would you use Azure SQL instead" half of this story already sitting in a real, working sibling project — worth pointing at directly in an interview rather than re-explaining relational databases from scratch.

## What's real vs. reference-only in this repo

The code compiles cleanly and matches the real `Microsoft.Azure.Cosmos` SDK surface (`CosmosClient`, `PartitionKey`, `ReadItemAsync`, `GetItemQueryIterator`, `RequestCharge`). **Nothing here was run against a real Cosmos DB account or the Cosmos DB Emulator** — the emulator is a heavyweight Windows-only install not attempted in this environment, and no real Cosmos account was available. This session's separate Smart App Control restriction (`docs/ARCHITECTURE.md`) would have blocked running the app locally regardless, even if a Cosmos backend had been available to point at.

---

## Interview Questions

**Q: When would you pick Cosmos DB over Azure SQL, and vice versa?**
Azure SQL when you need multi-table transactions, joins, strict relational integrity, and your data's shape is stable and well-modeled up front. Cosmos DB when you need to scale horizontally to very large or globally-distributed workloads, your document shape varies or evolves, and you're willing to design around a partition key and denormalize instead of joining.

**Q: What is a partition key and why does choosing it matter so much?**
It's the property Cosmos uses to physically distribute your data across partitions. A good choice has high cardinality (many distinct values, so data spreads evenly — not something like a boolean "status" flag with only two values, which would pile everything into two giant partitions) and matches your most common query's filter, so most reads become cheap point reads/single-partition queries instead of expensive cross-partition fan-outs. Choosing badly means either a "hot partition" (uneven load concentrated on one value) or most of your real-world queries being forced to scan everything.

**Q: What's the difference between a point read and a cross-partition query, cost-wise?**
A point read (partition key + id) goes directly to the one physical partition holding that item — cheap, low, predictable RU cost regardless of how much total data exists in the container. A cross-partition query has no partition key to route by, so Cosmos queries every partition and merges the results — RU cost scales with the number of partitions and how much each has to scan, and gets more expensive as the container grows even if the actual result set is small.

**Q: Can you change a container's partition key after creation?**
No — like blob types, it's fixed at creation. Changing your mind means creating a new container with the new partition key and migrating the data across, not flipping a setting.

**Q: Why does Cosmos DB favor denormalization over joins?**
There's no cross-container join operation in the Core (SQL) API — a query only ever operates within a single container. So instead of splitting related data across containers and joining at query time (the relational instinct), you embed what you'd need together into a single document, accepting some data duplication in exchange for every read being self-contained and fast. This is the same trade every NoSQL document store makes, not something specific to Cosmos.

**Q: What does "Request Unit (RU)" actually measure, and why does it matter for cost?**
An RU is Cosmos's normalized unit of throughput cost — roughly, the cost of reading a 1KB document by its id and partition key is about 1 RU, and every operation (writes, queries, cross-partition fan-outs) costs some multiple of that depending on complexity, document size, and how many partitions had to be touched. You provision (or auto-scale) RU/s capacity and get throttled (`429 Too Many Requests`) if you exceed it — so an expensive, poorly-partitioned query pattern doesn't just cost more money, it can directly cause throttling for every other operation sharing that capacity.
