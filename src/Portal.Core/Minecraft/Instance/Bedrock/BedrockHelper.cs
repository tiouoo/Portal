using System.Xml;
using System.Text.Json;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Entity;
using Portal.Core.Minecraft.Classes;

namespace Portal.Core.Minecraft.Instance.Bedrock;

public class BedrockHelper
{
    public static readonly string ConfigFolder = Path.Combine("config", "Portal");
    private const string InstanceConfigFileName = "instance.json";
    private const string PreloadConfigFileName = "config.json";
    private static readonly string LegacyConfigFolder = Path.Combine("config", "Portal.Desktop");
    public static (string Version,string PackName) GetInstanceVersion(string instanceFolder)
    {
        var manifestPath = Path.Combine(instanceFolder, "appxmanifest.xml");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException($"未找到 {manifestPath} 文件");

        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(File.ReadAllText(manifestPath));

        XmlNamespaceManager nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
        nsManager.AddNamespace("ns", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");

        XmlNode identityNode = xmlDoc.SelectSingleNode("//ns:Identity", nsManager);

        if (identityNode != null)
        {
            string version = identityNode.Attributes["Version"]?.Value
                ?? throw new InvalidDataException("Identity 节点缺少 Version 属性");
            string packName = identityNode.Attributes["Name"]?.Value
                ?? throw new InvalidDataException("Identity 节点缺少 Name 属性");

            return (version, packName);
        }
        else
        {
            throw new NullReferenceException("未找到 Identity 节点");
        }
    }
    
    public static BedrockInstanceConfig GetInstanceConfig(string instanceFolder)
    {
        if (InstanceManager.GetInstanceType(instanceFolder) != MinecraftInstanceType.Bedrock)
            throw new InvalidOperationException("指定的实例文件夹不是 Bedrock 实例");

        MigrateLegacyConfigFolder(instanceFolder);
        MigrateInstanceConfig(instanceFolder);
        var configFile = Path.Combine(instanceFolder, ConfigFolder, InstanceConfigFileName);
        ConfigEntity<BedrockInstanceConfig> configEntity;

        if (!File.Exists(configFile))
        {
            configEntity = new(configFile);
            configEntity.Data = new()
            {
                Name = Path.GetFileName(instanceFolder),
                Version = GetInstanceVersion(instanceFolder).Version,
                Description = string.Empty,
                BuildType = File.Exists(Path.Combine(instanceFolder, "MicrosoftGame.Config"))
                    ? BedrockBuildType.GDK
                    : BedrockBuildType.UWP,
                Type = GetVersionTypeWithPackName(GetInstanceVersion(instanceFolder).PackName)
            };
            configEntity.Save();
        }
        else
            configEntity = new(configFile);

        var result = configEntity.Data;
        result.InstancePath = instanceFolder;
        return result;
    }

    public static void SaveInstanceConfig(BedrockInstanceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        MigrateLegacyConfigFolder(config.InstancePath);
        MigrateInstanceConfig(config.InstancePath);
        var configFile = Path.Combine(config.InstancePath, ConfigFolder, InstanceConfigFileName);
        new ConfigEntity<BedrockInstanceConfig>(configFile) { Data = config }.Save();
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
            // Only move Portal instance metadata; config.json now belongs to PreloadCpp.
            if (document.RootElement.TryGetProperty("name", out _))
                File.Move(preloadConfigFile, instanceConfigFile);
        }
        catch (JsonException)
        {
            // Invalid legacy data is handled by ConfigEntity when a new instance config is created.
        }
    }

    public static BedrockInstanceReleaseType GetVersionTypeWithPackName(string packName)
    {
        if (string.IsNullOrEmpty(packName)) return BedrockInstanceReleaseType.Release;

        if (packName.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
            packName.Contains("beta", StringComparison.OrdinalIgnoreCase))
        {
            return BedrockInstanceReleaseType.Preview;
        }

        return BedrockInstanceReleaseType.Release;
    }
}
