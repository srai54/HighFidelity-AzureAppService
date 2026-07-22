using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Identity.Web;
using WebApp.Configuration;
using WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration providers ──
// CreateBuilder(args) has ALREADY registered the default configuration
// providers, in this order (later ones override earlier ones for the same key):
//   1. appsettings.json
//   2. appsettings.{Environment}.json   (e.g. appsettings.Development.json)
//   3. User Secrets                     (Development only)
//   4. Environment variables            (App Service app settings arrive here)
//   5. Command-line args
// Everything merges into ONE builder.Configuration (an IConfiguration). You can
// ADD MORE sources with builder.Configuration.Add*(...). Below we add an extra,
// optional JSON file to make the "config is a layered stack of sources" idea
// concrete — reloadOnChange means edits are picked up without a restart, and
// optional:true means the app still starts fine when the file doesn't exist.
// (AddAzureKeyVault in the Key Vault block below is just another Add* provider,
// added last so its values win — see docs/ICONFIGURATION.md and docs/KEYVAULT.md.)
builder.Configuration.AddJsonFile("appsettings.custom.json", optional: true, reloadOnChange: true);

// ── Key Vault ──
// Only wired up when a real Vault URI is configured (i.e. running in Azure
// with KeyVault:Uri set as an App Service application setting). Locally,
// with no URI configured, this is skipped entirely and the same config keys
// (PaymentGateway:ApiKey etc.) are read from appsettings.Development.json /
// User Secrets instead — IOptions binding downstream doesn't change either
// way. See docs/KEYVAULT.md for the naming-convention gotcha
// (Key Vault secret "PaymentGateway--ApiKey" -> config key "PaymentGateway:ApiKey").
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    // DefaultAzureCredential tries, in order: environment variables, Managed
    // Identity (when actually running in Azure), then a handful of local
    // dev credentials (Azure CLI, Visual Studio, etc.) — no secret of its
    // own to configure, which is the point of using it here.
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

builder.Services.Configure<PaymentGatewayOptions>(
    builder.Configuration.GetSection(PaymentGatewayOptions.SectionName));

// ── Application Insights ──
// Connection string comes from config (APPLICATIONINSIGHTS_CONNECTION_STRING
// / ApplicationInsights:ConnectionString) — App Service sets this
// automatically when Application Insights is linked to the App Service
// resource. Unlike Key Vault/Service Bus above, this one does NOT degrade
// gracefully with nothing configured: AddApplicationInsightsTelemetry()
// throws InvalidOperationException("A connection string was not found.")
// at startup if there's no connection string anywhere. So the conditional
// guard here isn't a style choice, it's load-bearing. See
// docs/APPLICATION_INSIGHTS.md.
var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

// ── Blob Storage ──
// "UseDevelopmentStorage=true" is Azurite's well-known local connection
// string — same SDK, same code, against a local emulator instead of a real
// storage account. Swap Storage:ConnectionString for a real account (or a
// blob endpoint URI + DefaultAzureCredential, to avoid a connection string
// entirely) when actually deploying.
var storageConnectionString = builder.Configuration["Storage:ConnectionString"] ?? "UseDevelopmentStorage=true";
builder.Services.AddSingleton(new BlobServiceClient(storageConnectionString));
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();

// ── Service Bus ──
// No local emulator for this one (Azurite only covers Blob/Queue/Table
// Storage) — needs a real namespace's connection string in
// ServiceBus:ConnectionString to actually send. See docs/SERVICE_BUS.md.
var serviceBusConnectionString = builder.Configuration["ServiceBus:ConnectionString"];
if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
{
    builder.Services.AddSingleton(new ServiceBusClient(serviceBusConnectionString));
    builder.Services.AddSingleton<IServiceBusPublisher, ServiceBusPublisher>();
}

// ── Resilience (Polly, via Microsoft.Extensions.Http.Resilience) ──
// AddStandardResilienceHandler wires up a whole pipeline in one call: retry
// (exponential backoff + jitter) -> circuit breaker -> attempt timeout ->
// overall timeout. It's Polly v8 under the hood. This has nothing to depend
// on conditionally — there's no "no config" state, so unlike Key Vault /
// Service Bus above this is always registered. See docs/RESILIENCE_POLLY.md,
// and ResilienceController for a self-contained way to see it working.
builder.Services.AddHttpClient("ResilientClient", client =>
{
    var baseUrl = builder.Configuration["ResilienceDemo:TargetBaseUrl"] ?? "http://localhost:5175";
    client.BaseAddress = new Uri(baseUrl);
})
.AddStandardResilienceHandler();

// ── Managed Identity + Entra ID auth ──
// Only wired up when a real tenant is configured (AzureAd:TenantId) — same
// conditional shape as Key Vault above, for the same reason: nothing to
// point at locally without a real Azure AD / Entra ID app registration.
// AddMicrosoftIdentityWebApi validates incoming JWT bearer tokens issued by
// Entra ID (audience, issuer, signature against the tenant's public keys) —
// this secures *inbound* calls to this API. That's a different job from
// DefaultAzureCredential above, which is this API authenticating *outbound*
// to Key Vault/Storage/Service Bus. See docs/MANAGED_IDENTITY_ENTRA_ID.md for
// the full picture of both directions plus the Managed Identity story.
var azureAdTenantId = builder.Configuration["AzureAd:TenantId"];
if (!string.IsNullOrWhiteSpace(azureAdTenantId))
{
    builder.Services.AddAuthentication(Microsoft.Identity.Web.Constants.Bearer)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
}

builder.Services.AddAuthorization();

// ── Redis Cache ──
// No local Redis available in this environment (no Docker), so this falls
// back to AddDistributedMemoryCache — an in-process, non-shared stand-in
// that implements the same IDistributedCache interface. Application code
// (CacheDemoController) is written against IDistributedCache either way and
// doesn't know or care which backing store is behind it. See
// docs/REDIS_CACHE.md.
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

// ── Cosmos DB ──
// Only registered when a real account (or the Cosmos DB Emulator) connection
// string is configured — no local emulator was available in this environment
// (it's a heavyweight Windows-only install, not attempted here). Same
// conditional shape as Service Bus above. See docs/SQL_VS_COSMOS.md.
var cosmosConnectionString = builder.Configuration["Cosmos:ConnectionString"];
if (!string.IsNullOrWhiteSpace(cosmosConnectionString))
{
    builder.Services.AddSingleton(new Microsoft.Azure.Cosmos.CosmosClient(cosmosConnectionString));
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── Middleware pipeline ──
// Order matters: each middleware runs in the order added, wrapping the next.
// UseStaticFiles serves files straight from wwwroot/ (e.g. wwwroot/index.html
// -> GET /index.html) WITHOUT hitting a controller — it short-circuits the
// pipeline for matching file paths, which is exactly why it's placed early,
// before routing/auth: no point running auth for a static asset. See
// docs/ICONFIGURATION.md for the pipeline explanation.
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));

if (!string.IsNullOrWhiteSpace(azureAdTenantId))
{
    app.UseAuthentication();
}

app.UseAuthorization();
app.MapControllers();
app.Run();
