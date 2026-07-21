using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using WebApp.Configuration;
using WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseAuthorization();
app.MapControllers();
app.Run();
