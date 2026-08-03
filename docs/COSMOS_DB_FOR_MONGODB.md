# Azure Cosmos DB for MongoDB — the Mongo-Compatible API, and RU vs. vCore

> Read `docs/MONGODB.md` first if the replica-set/sharding/shard-key vocabulary below isn't already familiar — this doc assumes it and focuses only on what Azure Cosmos DB adds/changes on top.

## The plain-English version

`docs/SQL_VS_COSMOS.md` covers Cosmos DB through its **Core (SQL) API** — JSON documents, queried with a SQL-like syntax, read through the `Microsoft.Azure.Cosmos` SDK. That's one of several **APIs** Cosmos DB exposes on top of the same underlying platform. **Azure Cosmos DB for MongoDB** is a different one: instead of the Core API's SQL-like query surface, it speaks the actual **MongoDB wire protocol**, so existing MongoDB drivers, tools (Compass, `mongodump`/`mongorestore`), and application code can point at it with little or no rewrite. The pitch is specifically for teams that already have a MongoDB app (self-hosted, or on MongoDB Atlas) and want Azure's management/scaling without rewriting every query.

There are actually **two distinct architectures** hiding under that one product name, and the difference is the thing interviewers probe for:

1. **RU-based** (the original, still what most people mean historically by "Cosmos DB API for MongoDB"). This runs on Cosmos's own storage engine — the same one the Core API uses — with a MongoDB-wire-protocol *translation layer* on top. Billing, throttling, and scaling all work in **Request Units (RUs)**, exactly like the Core API. It supports a defined subset of real MongoDB server versions/behavior (not 100% fidelity), because it's emulating Mongo semantics on a different engine underneath.
2. **vCore-based** (newer, GA in 2023). This is a genuinely different backend: real MongoDB replica-set architecture, provisioned as **vCore compute tiers** (like renting VM-shaped database capacity) rather than RUs, aiming for much higher fidelity to actual MongoDB — richer aggregation pipeline support, multi-document transactions, native indexing behavior — because it isn't translating onto a foreign engine underneath.

The practical decision: RU-based if you're already deep in the Cosmos ecosystem and want one consistent billing/scaling model (RUs) across both your SQL-API and Mongo-API workloads. vCore-based if fidelity to real MongoDB behavior matters more than that consistency — e.g., a lift-and-shift from Atlas where subtle Mongo-specific behavior is relied upon.

## The analogy that makes it click

Think of "Azure Cosmos DB for MongoDB" as **a foreign-language phrasebook vs. actually being fluent**.

**RU-based** is the phrasebook: you speak MongoDB's language (the wire protocol) to a listener (Cosmos's Core engine) who doesn't natively think that way — it translates on the fly. Most everyday phrases work fine, but idioms and grammar particular to the native language don't always translate perfectly, and the *way you're billed for the conversation* (RUs) is native to the listener, not to the language you're speaking.

**vCore-based** is a native speaker: the engine underneath genuinely *is* a MongoDB-behaving system (real replica sets, real read/write concerns), so idioms translate correctly — but you're now paying for a dedicated native speaker's time (provisioned vCore compute), not a shared per-phrase translation fee (RUs).

## How this relates to the repo

This repo's Cosmos code (`src/WebApp/Controllers/CosmosDemoController.cs`, `src/WebApp/Program.cs`) uses the **Core (SQL) API only** — a `CosmosClient` from `Microsoft.Azure.Cosmos`, `PartitionKey`, `ReadItemAsync`, `GetItemQueryIterator`. There is no MongoDB-API code here: no `MongoClient`, no `Microsoft.Azure.Cosmos.MongoDB`-flavored connection string, nothing provisioned via `az cosmosdb mongodb ...`. This doc is **conceptual only** — the same "theory + interview Q&A, no dedicated code" treatment this repo gives `MESSAGING_COMPARISON.md` and `DEPLOYMENT_SLOTS_SCALING.md` — included because "Cosmos DB has a MongoDB API too, and here's what's actually different about it" is a question that comes up immediately after any Cosmos DB discussion, right alongside partition keys.

The one concrete link back to real code: the **partition key** concept in `docs/SQL_VS_COSMOS.md` (`customerId`, chosen at container creation, drives which physical partition a query hits) is the *exact same idea* as the **shard key** in Cosmos DB for MongoDB — same physical-distribution mechanics, same "fixed at creation, can't casually change it later" rule, same cost cliff between a query that includes it (cheap) and one that doesn't (expensive fan-out). If `CosmosDemoController`'s point-read-vs-cross-partition-query demo makes sense, the MongoDB-API version of that story is identical with "partition key" swapped for "shard key."

## RU-based vs. vCore-based, side by side

| | RU-based | vCore-based |
|---|---|---|
| Underlying engine | Cosmos's own storage engine, with a Mongo wire-protocol translation layer | A real MongoDB-behaving replica-set architecture |
| Billing/scaling unit | Request Units (RUs) — same model as the Core API | Provisioned vCore compute + storage tiers — like sizing a VM |
| Fidelity to real MongoDB | Partial — a defined feature/version subset | High — much closer to self-hosted MongoDB/Atlas behavior |
| Consistency model | Cosmos's 5 consistency levels (Strong, Bounded Staleness, Session, Consistent Prefix, Eventual) — leaks through even though you're speaking Mongo's protocol, because consistency is a property of the engine, not the wire protocol | Native MongoDB read/write concerns (e.g., `majority`, `local`) — because the engine itself is Mongo-shaped |
| Best fit | Already in the Cosmos/RU ecosystem, want one billing model across SQL + Mongo APIs | Migrating an existing MongoDB app where exact behavior/feature parity matters |
| Sharding key concept | Partition key (identical mechanics to the Core API) | Shard key (same idea, MongoDB-native terminology) |

## What's real vs. reference-only in this repo

Nothing here is code-backed — this is a pure theory doc, same as `MESSAGING_COMPARISON.md`. No Cosmos DB for MongoDB account, RU-based or vCore-based, was provisioned or connected to in this environment. Pair this with `docs/SQL_VS_COSMOS.md` (the Core API, which the repo *does* have real — if untested-against-a-live-account — code for) for the fuller Cosmos picture.

---

## Interview Questions

**Q: What's the actual difference between the Cosmos DB "Core (SQL) API" and "API for MongoDB"?**
Same underlying platform (accounts, databases/containers, global distribution, partitioning), different **query surface/wire protocol** exposed on top. The Core API speaks a SQL-like query language over JSON documents via the Cosmos SDK. The MongoDB API speaks the actual MongoDB wire protocol, so existing MongoDB drivers and tools work against it with little to no code change — the selling point is compatibility with an app that already speaks Mongo, not a different set of underlying capabilities.

**Q: What's the difference between RU-based and vCore-based Cosmos DB for MongoDB?**
RU-based is the original offering: Cosmos's own storage engine with a MongoDB-protocol translation layer on top, billed and throttled in Request Units exactly like the Core API, with a defined subset of real MongoDB feature/version fidelity. vCore-based is a genuinely different, newer backend built as an actual MongoDB-behaving replica-set architecture, billed by provisioned vCore compute/storage instead of RUs, aiming for much higher fidelity to real MongoDB semantics (richer aggregation pipeline support, multi-document transactions, native read/write concerns).

**Q: Why would fidelity to "real" MongoDB behavior differ between the two, if both claim MongoDB compatibility?**
Because RU-based is *translating* MongoDB's wire protocol onto Cosmos's own native engine — semantics that don't map cleanly onto that engine (certain aggregation stages, transaction guarantees, index behaviors) end up as a subset or an approximation. vCore-based doesn't have that translation gap because the engine underneath genuinely behaves like MongoDB rather than being a different database wearing a MongoDB-shaped protocol on top.

**Q: If Cosmos DB for MongoDB speaks the MongoDB protocol, why does the RU-based version still expose Cosmos's consistency levels instead of MongoDB's read/write concerns?**
Because consistency guarantees are a property of the **storage engine actually doing the replication**, not of the wire protocol used to talk to it. RU-based Cosmos DB for MongoDB still runs on Cosmos's own engine under the translation layer, so it inherits that engine's five consistency levels (Strong, Bounded Staleness, Session, Consistent Prefix, Eventual) rather than MongoDB's native concept of read/write concerns. vCore-based, running an actual MongoDB-behaving engine, exposes real MongoDB read/write concerns instead — a clean tell for which architecture you're actually talking to.

**Q: How does the "shard key" in Cosmos DB for MongoDB relate to the "partition key" from the Core API?**
Same concept, same mechanics, different terminology inherited from MongoDB's own vocabulary. Cosmos physically distributes documents across partitions based on this key's value regardless of which API you're using — a query that includes it goes straight to the right partition/shard (cheap); one that doesn't fans out across all of them (expensive). It's fixed at collection/container creation in both APIs — changing it means migrating to a new collection, not flipping a setting.

**Q: When would you pick Cosmos DB for MongoDB over the Core (SQL) API, given they run on the same underlying platform?**
When you already have a MongoDB application, driver code, or tooling (Compass, `mongodump`/`mongorestore`, an existing team fluent in Mongo query syntax) and want Azure's managed scaling/global distribution without rewriting every query to the Core API's SQL-like syntax. It's a migration-friendliness decision, not a capability difference — greenfield projects with no existing MongoDB investment have less reason to prefer it over the Core API.
