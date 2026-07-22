# Provisioning — Creating the Azure Resources (learning reference)

## Read this first

The `scripts/` folder contains **az CLI provisioning scripts, one per feature**. They are a **learning reference** — commented so that reading them teaches you what each Azure resource is and how it connects to the app code. **Nothing in this repo runs them.** They create real, billable resources *only when you run them yourself*, deliberately, on your own machine after `az login`.

This pairs the "how do I *create* the Azure resource" story with the existing per-topic docs (which cover the theory + the app code + interview Q&A). Read them together: `scripts/01-keyvault.azcli` alongside `docs/KEYVAULT.md`, and so on.

Every script's comments follow the same shape: **theory** (what the resource is, why it exists) → **the commands** (what to create, with the cost tier chosen) → **how it wires into the app** (which app setting / connection string) → **how to verify** it worked.

## The order to run them

Dependencies exist, so run them roughly in numbered order:

| # | Script | Creates | Pairs with | Cost |
|---|---|---|---|---|
| 00 | `00-resource-group.azcli` | Resource group + shared vars | — | free |
| 01 | `01-keyvault.azcli` | Key Vault + secrets + access grant | `KEYVAULT.md` | free |
| 02 | `02-app-insights.azcli` | Log Analytics workspace + App Insights | `APPLICATION_INSIGHTS.md` | free (5 GB/mo) |
| 03 | `03-app-service.azcli` | App Service plan (F1) + Web App + Managed Identity + deploy | `DEPLOYMENT_SLOTS_SCALING.md` | **free (F1)** |
| 04 | `04-functions.azcli` | Storage + Consumption Function App + deploy | `FUNCTION_APPS.md`, `DURABLE_FUNCTIONS.md` | free (1M exec/mo) |
| 05 | `05-service-bus.azcli` | Service Bus namespace + `orders` queue | `SERVICE_BUS.md`, `MESSAGING_COMPARISON.md` | ~pennies |
| 06 | `06-blob-storage.azcli` | Storage account + container | `BLOB_STORAGE.md` | ~pennies |
| 07 | `07-redis.azcli` | Azure Cache for Redis (Basic C0) | `REDIS_CACHE.md` | **~$16/mo — only paid item** |
| 08 | `08-cosmos.azcli` | Cosmos DB account (free tier) + db + container | `SQL_VS_COSMOS.md` | free tier (1000 RU/s) |
| 09 | `09-entra-id.azcli` | Entra ID app registration | `MANAGED_IDENTITY_ENTRA_ID.md` | free |
| 99 | `99-teardown.azcli` | **Deletes the resource group + everything in it** | — | stops all billing |

01 and 02 come before 03 because the Web App wants their values as app settings at deploy time. 03 comes before 01's step 3 (the access grant) because the grant needs the app's Managed Identity to exist first — 01 and 03 are the one genuinely interleaved pair; the script comments call this out.

## Cost summary against a $200 credit

- **Everything except Redis is free-tier or pennies.** The free-tier / demo setup (skip Redis, use Cosmos free tier) runs at effectively **$0–3/month**.
- **Redis (Basic C0) is the only non-free item, ~$16/month** — create it only when you actually want to demo caching, and let `99-teardown.azcli` remove it when done.
- Even deploying *literally everything including Redis* is roughly **$16–20/month**, ~10% of a $200 credit over a full month.
- **The single most important habit:** everything lives in one resource group (`rg-highfidelity-learning`); running `99-teardown.azcli` deletes it and stops all billing instantly. Do that whenever you're not actively using the deployment.

## The app-settings wiring, in one place

Each feature "turns on" in the deployed app when its config key is present (and degrades gracefully when absent — the pattern throughout `Program.cs`). In Azure, app settings use `__` (double-underscore) where .NET config uses `:`.

| App setting (Azure) | Reads as | Set by | Feature it enables |
|---|---|---|---|
| `KeyVault__Uri` | `KeyVault:Uri` | 03 (value from 01) | Real Key Vault |
| `ApplicationInsights__ConnectionString` | `ApplicationInsights:ConnectionString` | 03 (value from 02) | Telemetry |
| `Storage__ConnectionString` | `Storage:ConnectionString` | 03 (value from 06) | Blob Storage |
| `ServiceBus__ConnectionString` | `ServiceBus:ConnectionString` | 03 (value from 05) | Service Bus publisher |
| `ServiceBusConnection` | (binding name, not `:`-mapped) | 04 (value from 05) | Service Bus-triggered function |
| `Redis__ConnectionString` | `Redis:ConnectionString` | 03 (value from 07) | Real Redis cache |
| `Cosmos__ConnectionString` | `Cosmos:ConnectionString` | 03 (value from 08) | Cosmos DB |
| `AzureAd__TenantId` / `__ClientId` / `__Instance` | `AzureAd:*` | 03 (values from 09) | Entra ID inbound auth |

## Honesty note

These scripts were **written as a correct reference, not executed** — this environment has no Azure subscription, and (as documented in `docs/ARCHITECTURE.md`) a Smart App Control policy currently blocks running the app locally too. The commands follow current az CLI syntax and the standard provisioning flow, but you should expect to hit the occasional real-world wrinkle when you run them (a name already taken, an RBAC assignment needing a minute to propagate, Redis taking 15–20 min to provision) — the scripts flag the ones that are predictable.
