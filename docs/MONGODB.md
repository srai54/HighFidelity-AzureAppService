# MongoDB — Document Database Fundamentals

## The plain-English version

MongoDB is a general-purpose **document database**: data is stored as BSON (a binary superset of JSON) documents, grouped into **collections**, grouped into **databases** — the rough equivalents of rows-in-a-table and tables-in-a-schema, except documents in the same collection don't need the same fields. Every document gets a unique `_id` (a primary key, auto-generated as an `ObjectId` if you don't supply one). Beyond that single required field, the schema is whatever your application decides at write time, and it can evolve document-by-document without an `ALTER TABLE`-style migration.

Three mechanisms matter most for how MongoDB actually behaves in production, and they're easy to conflate with each other:

1. **Indexes** — same purpose as in a relational database: avoid scanning every document to satisfy a query. Single-field, compound (multiple fields, order matters), multi-key (indexing an array field), and text indexes are all supported. A query that can't use an index does a full collection scan — the single most common "why is this slow" answer.
2. **Replica sets** — for **high availability**, not scale. A replica set is one **primary** (accepts all writes) plus several **secondaries** that continuously replicate the primary's oplog. If the primary goes down, the remaining members hold an election and promote a new primary automatically — the point is surviving a node failure, not spreading load across more capacity. Reads can optionally be routed to secondaries, but that's a choice you make for read scaling, not something replica sets do automatically.
3. **Sharding** — for **horizontal scale**, not availability (though each shard is typically itself a replica set, so you get both together in practice). A sharded cluster splits a collection's documents across multiple shards based on a **shard key**, chosen once and not casually changed later, with a `mongos` router directing each query to the right shard(s) and config servers tracking which data lives where. This is the same physical-distribution idea as a Cosmos DB partition key — pick a high-cardinality field matching your dominant query pattern, or pay for cross-shard fan-outs.

**Read/write concerns** are MongoDB's tunable-consistency knobs. A write concern like `{ w: "majority" }` means "don't acknowledge this write until a majority of replica set members have it" — durability against a single node failure, at some latency cost. A read concern like `"majority"` means "only return data that's been acknowledged by a majority" — avoiding reads of data that could still be rolled back if the primary hasn't fully replicated yet. This is MongoDB's version of the availability/consistency trade-off: stronger concerns cost latency, weaker ones risk staleness or (rarely) reading data that gets rolled back.

**Multi-document ACID transactions** (since MongoDB 4.0 for replica sets, 4.2 for sharded clusters) let you wrap multiple document writes — even across collections — in a single atomic transaction, closing the historical gap with relational databases. They exist and work, but they cost more than the default single-document atomic write (every write to a single document is always atomic on its own, transaction or not), so the idiomatic MongoDB pattern is still to *embed* related data into one document where possible and reach for a real multi-document transaction only when that's not an option.

## The analogy that makes it click

A MongoDB collection is a **drawer of index cards** — every card (document) can have a different set of fields written on it, unlike a spreadsheet where every row must have the same columns. An index is a **sorted card catalog** pointing at which cards have a given value, so you don't have to flip through the whole drawer.

A **replica set** is keeping identical photocopies of the whole cabinet at several offices: one office is "primary" — all edits happen there first and get faxed to the others — and if that office burns down, the remaining offices vote for a new primary and carry on. Nothing about this makes the cabinet hold more cards; it just means one fire doesn't lose your data or stop the business.

**Sharding** is the opposite move: instead of copying the *whole* cabinet everywhere, you split the *drawers* themselves across offices — Office A holds every card whose customer ID starts with A–M, Office B holds N–Z (a shard key). Now no single office needs room for every card, but asking a question that doesn't mention the shard key means calling every office and asking them all to check their drawers — the cross-shard fan-out, same expensive pattern as a Cosmos cross-partition query.

## How this relates to the repo

No dedicated code in this repo — this is a foundational, Azure-agnostic reference, kept separate on purpose from `docs/COSMOS_DB_FOR_MONGODB.md`. That doc covers **Azure Cosmos DB's MongoDB-compatible API** (RU-based vs. vCore-based, and what leaks through from Cosmos's own engine even while speaking Mongo's wire protocol) — this doc covers **MongoDB itself**, the thing that API is compatible with. Read this one first if the replica-set/sharding/shard-key vocabulary in the Cosmos doc doesn't already click; the Cosmos doc assumes it.

The one place this connects to real code in this repo: `src/WebApp/Controllers/CosmosDemoController.cs` uses Cosmos's **Core (SQL) API**, not MongoDB — its partition key (`customerId`) is the Cosmos-native equivalent of a MongoDB shard key, and the point-read-vs-cross-partition-query cost story it demonstrates is the same physical-distribution story described above for sharding.

## What's real vs. reference-only in this repo

Pure theory doc — no MongoDB instance (self-hosted, Atlas, or Cosmos DB for MongoDB) was run against in this environment.

---

## Interview Questions

**Q: How does MongoDB's document model differ from a relational table?**
A collection doesn't enforce a fixed schema across its documents — each document is a self-contained BSON object that can have different fields, nested objects, and arrays, and the only universally required field is `_id`. This trades the relational database's enforced structure and cross-table joins for flexibility and (typically) denormalization — you embed related data into one document rather than joining across collections, since MongoDB's query language isn't built around multi-collection joins the way SQL is.

**Q: What's the difference between a replica set and a sharded cluster, and why do people conflate them?**
A replica set exists for **availability** — it's the same data copied across multiple nodes so a node failure doesn't take down the database (one primary takes writes, secondaries replicate it, an election promotes a new primary on failure). A sharded cluster exists for **horizontal scale** — it splits the data itself across multiple shards so no single node needs to hold the whole dataset. They get conflated because a real production sharded cluster typically makes each shard its own replica set, so you're usually looking at both mechanisms deployed together — but they solve different problems and neither one substitutes for the other.

**Q: What is a shard key, and what happens if you pick a bad one?**
The field MongoDB uses to decide which shard a document lives on, chosen when a collection is sharded and not easily changed afterward. A bad choice — low cardinality, or one that doesn't match your actual query patterns — causes either a "hot shard" (most writes/reads concentrate on one shard because most documents share a value) or forces most real queries into an expensive scatter-gather across every shard, because the query didn't include the one field that would have routed it directly. This is the same design problem, and the same stakes, as choosing a Cosmos DB partition key.

**Q: What do read concern and write concern actually control?**
Write concern controls how many replica set members must acknowledge a write before MongoDB confirms it succeeded (e.g., `w: "majority"` — safer, more latency; `w: 1` — just the primary, faster, riskier if it fails before replicating). Read concern controls the durability guarantee of the data a read returns (e.g., `"majority"` — only data acknowledged by a majority, so it won't vanish if the primary rolls back; `"local"` — whatever the node you hit has locally, possibly not yet replicated). Together they're how you tune MongoDB along the latency-vs-durability/consistency spectrum per operation, rather than the whole database being locked into one setting.

**Q: Are individual writes in MongoDB atomic without an explicit transaction?**
Yes — a write to a single document is always atomic, transaction or not, which is why the idiomatic MongoDB design pattern is to embed related data into one document wherever reasonable: you get atomicity "for free" at the document level without needing a transaction. Multi-document ACID transactions (since 4.0/4.2) exist for the cases embedding can't cover — writes that genuinely span multiple documents or collections — but they carry more overhead than the default per-document atomicity, so they're the exception path, not the default one.

**Q: How does a MongoDB shard key compare to a Cosmos DB partition key?**
They're the same mechanism under different vocabulary: a field chosen at creation time that MongoDB (or Cosmos) uses to physically distribute documents, where a query that includes it routes directly to the right shard/partition (cheap) and a query that doesn't fans out across all of them (expensive). This is exactly the concept `docs/COSMOS_DB_FOR_MONGODB.md` and `docs/SQL_VS_COSMOS.md` build on for Cosmos DB specifically — the underlying idea doesn't change between MongoDB and Cosmos, only the name.
