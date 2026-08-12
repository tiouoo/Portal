namespace Portal.Core;

public static class ServiceCredentials
{
    public const string CurseForgeApiKeyEnvironmentVariable = "CURSEFORGE_API_KEY";
    public const string MicrosoftClientIdEnvironmentVariable = "MICROSOFT_CLIENT_ID";
    public const string GravityConeUptimeApiKeyEnvironmentVariable = "GRAVITYCONE_UPTIME_API_KEY";
    public const string CnbUpdateTokenEnvironmentVariable = "CNB_UPDATE_TOKEN";
    private const string CurseForgeApiKeyMetadataKey = "Portal.CurseForgeApiKey";
    private const string MicrosoftClientIdMetadataKey = "Portal.MicrosoftClientId";
    private const string GravityConeUptimeApiKeyMetadataKey = "Portal.GravityConeUptimeApiKey";
    private const string CnbUpdateTokenMetadataKey = "Portal.CnbUpdateToken";

    public static string? CurseForgeApiKey => Get(CurseForgeApiKeyMetadataKey, CurseForgeApiKeyEnvironmentVariable);

    public static string? GravityConeUptimeApiKey =>
        Get(GravityConeUptimeApiKeyMetadataKey, GravityConeUptimeApiKeyEnvironmentVariable);

    public static string MicrosoftClientId => Get(MicrosoftClientIdMetadataKey, MicrosoftClientIdEnvironmentVariable)
        ?? throw new InvalidOperationException(
            $"Microsoft login requires {MicrosoftClientIdEnvironmentVariable} to be set when building or running Portal.");

    public static string? CnbUpdateToken => Get(CnbUpdateTokenMetadataKey, CnbUpdateTokenEnvironmentVariable);

    private static string? Get(string metadataKey, string environmentVariable)
    {
        var embedded = typeof(ServiceCredentials).Assembly
            .GetCustomAttributes(false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == metadataKey)?.Value;
        if (!string.IsNullOrWhiteSpace(embedded))
            return embedded;

        var value = Environment.GetEnvironmentVariable(environmentVariable)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
