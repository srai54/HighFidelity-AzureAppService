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

## Azure Service Bus

- 📺 **YouTube playlist:** https://www.youtube.com/playlist?list=PLU1w_BFZFd2pW5mcM_aTKCWwJw2sGBA20
- 📄 In-repo notes: [`docs/SERVICE_BUS.md`](SERVICE_BUS.md), [`docs/MESSAGING_COMPARISON.md`](MESSAGING_COMPARISON.md)
- 🔧 Code: `src/WebApp/Services/ServiceBusPublisher.cs`, `Controllers/OrdersController.cs` (publisher); `src/Functions/OrderCreatedFunction.cs` (receiver)
- 🛠️ Provisioning: [`scripts/05-service-bus.azcli`](../scripts/05-service-bus.azcli)

## Azure Storage (Blob, Queue, File)

- 📺 **YouTube playlist:** https://www.youtube.com/playlist?list=PLU1w_BFZFd2oCFnnuCv9AeD2ccsWM59My
- 📰 **Article (read-ready):** Understanding Azure Blob Storage basics — https://sedai.io/blog/understanding-azure-blob-storage-basics
- 📄 In-repo notes: [`docs/BLOB_STORAGE.md`](BLOB_STORAGE.md) (blob types), [`docs/AZURE_STORAGE.md`](AZURE_STORAGE.md) (account, access levels, SAS, connecting, listing, Postman), [`docs/AZURE_STORAGE_QUEUE.md`](AZURE_STORAGE_QUEUE.md), [`docs/AZURE_FILE_SHARE.md`](AZURE_FILE_SHARE.md)
- 📑 Master index + interview question bank: [`docs/BLOB_STORAGE_STUDY_INDEX.md`](BLOB_STORAGE_STUDY_INDEX.md)
- 🔧 Code: `src/WebApp/Services/BlobStorageService.cs`, `Controllers/BlobStorageController.cs`
- 🛠️ Provisioning: [`scripts/06-blob-storage.azcli`](../scripts/06-blob-storage.azcli)

## Azure App Service — Deployment & CI/CD

- 📺 **YouTube video:** https://www.youtube.com/watch?v=v4QHcxLjZZM
- 📺 **YouTube video:** https://www.youtube.com/watch?v=e732g6Hqulc
- 📄 In-repo notes: [`docs/DEPLOYMENT_SLOTS_SCALING.md`](DEPLOYMENT_SLOTS_SCALING.md) (slots & scaling), [`docs/PROVISIONING.md`](PROVISIONING.md)
- 🔧 Deploy target: `src/WebApp` (App Service) — Publish from VS/VS Code, `az webapp deploy`, or a GitHub Actions / Azure DevOps pipeline

---

## Other topics (add references here as you find them)

- **Function Apps** — [`docs/FUNCTION_APPS.md`](FUNCTION_APPS.md), [`docs/DURABLE_FUNCTIONS.md`](DURABLE_FUNCTIONS.md)
- **Blob Storage** — [`docs/BLOB_STORAGE.md`](BLOB_STORAGE.md)
- **Resilience (Polly)** — [`docs/RESILIENCE_POLLY.md`](RESILIENCE_POLLY.md)
- **Managed Identity + Entra ID** — [`docs/MANAGED_IDENTITY_ENTRA_ID.md`](MANAGED_IDENTITY_ENTRA_ID.md)
- **Redis Cache** — [`docs/REDIS_CACHE.md`](REDIS_CACHE.md)
- **Azure SQL vs. Cosmos DB** — [`docs/SQL_VS_COSMOS.md`](SQL_VS_COSMOS.md)
- **MongoDB fundamentals** — [`docs/MONGODB.md`](MONGODB.md)
- **Cosmos DB for MongoDB (RU vs. vCore)** — [`docs/COSMOS_DB_FOR_MONGODB.md`](COSMOS_DB_FOR_MONGODB.md)
- **App Service slots & scaling** — [`docs/DEPLOYMENT_SLOTS_SCALING.md`](DEPLOYMENT_SLOTS_SCALING.md)
- **IConfiguration & middleware** — [`docs/ICONFIGURATION.md`](ICONFIGURATION.md)
