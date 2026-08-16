using System.Reflection;

namespace Portal.Core.Services;

public static class CredentialsService
{
    private const string CurseForgeApiKeyEnvironmentVariable = "CURSEFORGE_API_KEY";
    private const string CurseForgeApiKeyMetadataKey = "Portal.CurseForgeApiKey";

    private const string MicrosoftClientIdMetadataKey = "Portal.MicrosoftClientId";
    private const string MicrosoftClientIdEnvironmentVariable = "MICROSOFT_CLIENT_ID";

    public const string GravityConeUptimeApiKeyEnvironmentVariable = "GRAVITYCONE_UPTIME_API_KEY";
    private const string GravityConeUptimeApiKeyMetadataKey = "Portal.GravityConeUptimeApiKey";

    public const string CnbUpdateTokenEnvironmentVariable = "CNB_UPDATE_TOKEN";
    private const string CnbUpdateTokenMetadataKey = "Portal.CnbUpdateToken";

    public static string? CurseForgeApiKey =>
        GetValue(CurseForgeApiKeyMetadataKey, CurseForgeApiKeyEnvironmentVariable);

    public static string? GravityConeUptimeApiKey =>
        GetValue(GravityConeUptimeApiKeyMetadataKey, GravityConeUptimeApiKeyEnvironmentVariable);

    public static string MicrosoftClientId =>
        GetValue(MicrosoftClientIdMetadataKey, MicrosoftClientIdEnvironmentVariable);

    public static string? CnbUpdateToken => GetValue(CnbUpdateTokenMetadataKey, CnbUpdateTokenEnvironmentVariable);

    private static string? GetValue(string metadataKey, string environmentVariable)
    {
        var embedded = typeof(CredentialsService).Assembly
            .GetCustomAttributes(false)
            .OfType<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == metadataKey)?.Value;
        if (!string.IsNullOrWhiteSpace(embedded))
            return embedded;

        var value = Environment.GetEnvironmentVariable(environmentVariable)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}