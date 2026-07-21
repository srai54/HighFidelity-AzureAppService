namespace WebApp.Configuration;

/// <summary>
/// Stand-in for "a third-party integration's credentials" — the classic
/// Key Vault use case. ApiKey/ApiSecret are secrets (Key Vault in Azure,
/// User Secrets/appsettings.Development.json locally); BaseUrl isn't
/// sensitive, so it lives in plain appsettings.json either way. Binding
/// code doesn't know or care which source each value came from — see
/// docs/KEYVAULT.md.
/// </summary>
public class PaymentGatewayOptions
{
    public const string SectionName = "PaymentGateway";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
