using System.Text.Json;
using System.Xml;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;

namespace Portal.Core.Minecraft.Instance.Bedrock;

public class BedrockHelper
{
    private const string InstanceConfigFileName = "instance.json";
    private const string PreloadConfigFileName = "config.json";
    public static readonly string ConfigFolder = Path.Combine("config", "Portal");
    private static readonly string LegacyConfigFolder = Path.Combine("config", "Portal.Desktop");

    public static (string Version, string PackName) GetInstanceVersion(string instanceFolder)
    {
        var manifestPath = Path.Combine(instanceFolder, "appxmanifest.xml");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException(string.Format(CommonLanguageManager.Instance.bedrock_manifestNotFound.CurrentValue(), manifestPath));

        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(File.ReadAllText(manifestPath));

        var nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
        nsManager.AddNamespace("ns", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");

        var identityNode = xmlDoc.SelectSingleNode("//ns:Identity", nsManager);

        if (identityNode != null)
        {
            var version = identityNode.Attributes["Version"]?.Value
                          ?? throw new InvalidDataException(CommonLanguageManager.Instance.bedrock_identityMissingVersion.CurrentValue());
            var packName = identityNode.Attributes["Name"]?.Value
                           ?? throw new InvalidDataException(CommonLanguageManager.Instance.bedrock_identityMissingName.CurrentValue());

            return (version, packName);
        }

        throw new NullReferenceException(CommonLanguageManager.Instance.bedrock_identityNotFound.CurrentValue());
    }

    public static BedrockInstanceConfig GetInstanceConfig(string instanceFolder)
    {
        if (InstanceManager.GetInstanceType(instanceFolder) != MinecraftInstanceType.Bedrock)
            throw new InvalidOperationException(CommonLanguageManager.Instance.bedrock_notBedrockInstance.CurrentValue());

        MigrateLegacyConfigFolder(instanceFolder);
        MigrateInstanceConfig(instanceFolder);

        var configFile = Path.Combine(instanceFolder, ConfigFolder, InstanceConfigFileName);
        var configEntry = new JsonConfigEntry<BedrockInstanceConfig>(configFile);

        if (configEntry.Data.Name is null)
        {
            configEntry.Data = CreateDefaultConfig(instanceFolder);
            configEntry.Save();
        }

        configEntry.Data.InstancePath = instanceFolder;
        return configEntry.Data;
    }

    public static void SaveInstanceConfig(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        MigrateLegacyConfigFolder(config.InstancePath);
        MigrateInstanceConfig(config.InstancePath);
        var configFile = Path.Combine(config.InstancePath, ConfigFolder, InstanceConfigFileName);
        new JsonConfigEntry<BedrockInstanceConfig>(configFile) { Data = config }.Save();
    }

    private static BedrockInstanceConfig CreateDefaultConfig(string instanceFolder)
    {
        var (version, packName) = GetInstanceVersion(instanceFolder);
        return new BedrockInstanceConfig
        {
            Name = Path.GetFileName(instanceFolder),
            Version = version,
            Description = string.Empty,
            BuildType = File.Exists(Path.Combine(instanceFolder, "MicrosoftGame.Config"))
                ? BedrockBuildType.GDK
                : BedrockBuildType.UWP,
            Type = GetVersionTypeWithPackName(packName)
        };
    }

    private static void MigrateLegacyConfigFolder(string instanceFolder)
    {
        var configFolder = Path.Combine(instanceFolder, ConfigFolder);
        var legacyConfigFolder = Path.Combine(instanceFolder, LegacyConfigFolder);
        if (!Directory.Exists(configFolder) && Directory.Exists(legacyConfigFolder))
            Directory.Move(legacyConfigFolder, configFolder);
    }

    private static void MigrateInstanceConfig(string instanceFolder)
    {
        var configFolder = Path.Combine(instanceFolder, ConfigFolder);
        var instanceConfigFile = Path.Combine(configFolder, InstanceConfigFileName);
        var preloadConfigFile = Path.Combine(configFolder, PreloadConfigFileName);
        if (File.Exists(instanceConfigFile) || !File.Exists(preloadConfigFile))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(preloadConfigFile));

            if (document.RootElement.TryGetProperty("name", out _))
                File.Move(preloadConfigFile, instanceConfigFile);
        }
        catch (JsonException)
        {
        }
    }

    public static BedrockInstanceReleaseType GetVersionTypeWithPackName(string packName)
    {
        if (string.IsNullOrEmpty(packName)) return BedrockInstanceReleaseType.Release;

        if (packName.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
            packName.Contains("beta", StringComparison.OrdinalIgnoreCase))
            return BedrockInstanceReleaseType.Preview;

        return BedrockInstanceReleaseType.Release;
    }
}