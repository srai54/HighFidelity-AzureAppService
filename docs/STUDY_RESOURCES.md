# Study Resources — External References

A single place to collect external learning material (videos, articles, docs)
per topic, alongside this repo's own `docs/*.md` notes. Add new links under the
matching topic heading as you find them.

> These are third-party links — content and availability are outside this repo's
> control. Pair them with the in-repo doc for each topic (linked below), which
> stays in sync with the actual code here.

---

## Application Insights (Azure Insights)

- 📺 **YouTube playlist:** https://www.youtube.com/playlist?list=PLU1w_BFZFd2p6-3yh5aZ6aeyPViDzZXWF
- 📄 In-repo notes: [`docs/APPLICATION_INSIGHTS.md`](APPLICATION_INSIGHTS.md)
- 🔧 Code: `src/WebApp/Program.cs` (conditional `AddApplicationInsightsTelemetry`)

## Azure Key Vault

- 📺 **YouTube playlist:** https://www.youtube.com/playlist?list=PLU1w_BFZFd2qrMfqa_z8nlnmu2KL8PK1T
- 📄 In-repo notes: [`docs/KEYVAULT.md`](KEYVAULT.md)
- 🔧 Code: `src/WebApp/Program.cs`, `Configuration/PaymentGatewayOptions.cs`, `Controllers/PaymentGatewayController.cs`
- 🛠️ Provisioning: [`scripts/01-keyvault.azcli`](../scripts/01-keyvault.azcli)

---

## Other topics (add references here as you find them)

- **Function Apps** — [`docs/FUNCTION_APPS.md`](FUNCTION_APPS.md), [`docs/DURABLE_FUNCTIONS.md`](DURABLE_FUNCTIONS.md)
- **Service Bus** — [`docs/SERVICE_BUS.md`](SERVICE_BUS.md), [`docs/MESSAGING_COMPARISON.md`](MESSAGING_COMPARISON.md)
- **Blob Storage** — [`docs/BLOB_STORAGE.md`](BLOB_STORAGE.md)
- **Resilience (Polly)** — [`docs/RESILIENCE_POLLY.md`](RESILIENCE_POLLY.md)
- **Managed Identity + Entra ID** — [`docs/MANAGED_IDENTITY_ENTRA_ID.md`](MANAGED_IDENTITY_ENTRA_ID.md)
- **Redis Cache** — [`docs/REDIS_CACHE.md`](REDIS_CACHE.md)
- **Azure SQL vs. Cosmos DB** — [`docs/SQL_VS_COSMOS.md`](SQL_VS_COSMOS.md)
- **App Service slots & scaling** — [`docs/DEPLOYMENT_SLOTS_SCALING.md`](DEPLOYMENT_SLOTS_SCALING.md)
- **IConfiguration & middleware** — [`docs/ICONFIGURATION.md`](ICONFIGURATION.md)
