using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Parser;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Instance.Java;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Instance;

public class InstanceManager
{
    private static InstanceManager? _instance;

    public static InstanceManager Instance
    {
        get { return _instance ??= new InstanceManager(); }
    }

    public ObservableCollection<MinecraftInstance> Instances { get; } = [];

    public List<string> VersionFolders { get; } = new() { "versions" };

    /// <summary>
    /// 当实例列表重新扫描完成时触发。
    /// </summary>
    public event EventHandler? InstancesChanged;

    /// <summary>
    /// 当实例统计数据发生变化时触发的事件
    /// </summary>
    public event EventHandler? StatisticsChanged;

    /// <summary>
    /// 当实例图标写入完成，需要刷新界面时触发。
    /// </summary>
    public event EventHandler<MinecraftInstance>? InstanceIconChanged;

    private InstanceManager()
    {
        if (OperatingSystem.IsWindows() ||
            OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
            VersionFolders.Add("bedrock_versions");
    }

    /// <summary>
    /// 通知统计数据已更新
    /// </summary>
    public void NotifyStatisticsChanged()
    {
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyInstanceIconChanged(MinecraftInstance instance)
    {
        InstanceIconChanged?.Invoke(this, instance);
    }

    public void RefreshAll(IEnumerable<MinecraftFolderEntry> folders)
    {
        var instances = ScanAll(folders);
        ApplyInstances(instances);
    }

    public List<MinecraftInstance> ScanAll(IEnumerable<MinecraftFolderEntry> folders)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new List<MinecraftInstance>();
        foreach (var folder in folders)
        {
            var folderPath = folder.FolderPath;
            if (!Directory.Exists(folderPath))
            {
                Logger.Warning($"Minecraft 文件夹不存在，跳过扫描：{folderPath}");
                continue;
            }

            Logger.Info($"正在扫描 Minecraft 文件夹：{folderPath}");
            var layout = folder.DetectedLayout;
            var instances = layout.Kind == MinecraftFolderKind.Standard
                ? new FolderScanner(layout.RootPath, folder.FolderName, VersionFolders).Scan()
                : ExternalMinecraftScanner.Scan(folder).ToList();
            result.AddRange(instances);
        }

        Logger.Info($"全部 Minecraft 实例扫描完成，共 {result.Count} 个实例，耗时 {stopwatch.ElapsedMilliseconds} ms。");
        return result;
    }

    public List<MinecraftInstance> ScanBedrock(IEnumerable<MinecraftFolderEntry> folders)
    {
        var result = new List<MinecraftInstance>();
        foreach (var folder in folders)
        {
            var layout = folder.DetectedLayout;
            if (layout.Kind is not (MinecraftFolderKind.Standard or MinecraftFolderKind.PortalMc))
                continue;

            var versionsPath = Path.Combine(layout.RootPath,
                layout.Kind == MinecraftFolderKind.PortalMc ? "bedrock_instances" : "bedrock_versions");
            if (!Directory.Exists(versionsPath))
                continue;

            foreach (var instanceFolder in Directory.GetDirectories(versionsPath))
            {
                try
                {
                    var config = BedrockHelper.GetInstanceConfig(instanceFolder);
                    result.Add(new MinecraftInstance(config, folder.FolderName, layout.RootPath));
                }
                catch (Exception exception)
                {
                    Logger.Error($"扫描基岩版实例失败：{instanceFolder}", exception);
                }
            }
        }

        return result;
    }

    public void ApplyInstances(IEnumerable<MinecraftInstance> instances)
    {
        var loadedInstances = instances as ICollection<MinecraftInstance> ?? instances.ToList();
        Logger.Info($"正在将扫描结果应用到实例列表，共 {loadedInstances.Count} 个实例。");
        Instances.Clear();
        foreach (var instance in loadedInstances)
            Instances.Add(instance);

        InstancesChanged?.Invoke(this, EventArgs.Empty);
        NotifyStatisticsChanged();
    }

    public static MinecraftInstanceType GetInstanceType(string instanceFolder)
    {
        if (File.Exists(Path.Combine(instanceFolder, "appxmanifest.xml")))
            return MinecraftInstanceType.Bedrock;
        if (File.Exists(Path.Combine(instanceFolder, $"{Path.GetFileName(instanceFolder)}.json")))
            return MinecraftInstanceType.Java;

        return MinecraftInstanceType.Java;
    }
}

internal class FolderScanner
{
    private readonly string _gameRootFolder;
    private readonly string _folderName;
    private readonly List<string> _versionFolders;

    public FolderScanner(string gameRootFolder, string folderName, List<string> versionFolders)
    {
        _gameRootFolder = gameRootFolder;
        _folderName = folderName;
        _versionFolders = versionFolders;
    }

    public List<MinecraftInstance> Scan()
    {
        var instances = new List<MinecraftInstance>();

        MinecraftParser minecraftParser = _gameRootFolder;
        var parsedJavaEntries = minecraftParser.GetMinecrafts();
        var internalBaseIds = parsedJavaEntries
            .OfType<ModifiedMinecraftEntry>()
            .Select(entry => entry.InheritedMinecraft)
            .Where(entry => entry is not null && HasClientVersionMetadata(entry))
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var javaEntries = parsedJavaEntries
            .Where(entry => !internalBaseIds.Contains(entry.Id))
            .ToDictionary(entry => entry.Id);

        var processedFolders = new HashSet<string>();

        foreach (var versionFolder in _versionFolders)
        {
            var versionsFolderPath = Path.Combine(_gameRootFolder, versionFolder);
            if (!Directory.Exists(versionsFolderPath))
            {
                if (versionFolder == "versions")
                    Directory.CreateDirectory(versionsFolderPath);
                continue;
            }

            foreach (var instanceFolder in Directory.GetDirectories(versionsFolderPath))
            {
                var folderKey = Path.GetFullPath(instanceFolder);
                if (processedFolders.Contains(folderKey))
                    continue;
                processedFolders.Add(folderKey);

                var instanceType = InstanceManager.GetInstanceType(instanceFolder);

                if (instanceType == MinecraftInstanceType.Java)
                {
                    // 单个实例异常（如配置文件损坏）不应中断整个扫描
                    try
                    {
                        var folderName = Path.GetFileName(instanceFolder);
                        if (javaEntries.TryGetValue(folderName, out var minecraftEntry))
                        {
                            instances.Add(new MinecraftInstance(minecraftEntry)
                            {
                                FolderName = _folderName,
                                FolderPath = _gameRootFolder
                            });
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error($"扫描 Java 实例失败：{instanceFolder}", e);
                    }
                }
                else if (instanceType == MinecraftInstanceType.Bedrock)
                {
                    try
                    {
                        var bedrockConfig = BedrockHelper.GetInstanceConfig(instanceFolder);
                        instances.Add(new MinecraftInstance(bedrockConfig, _folderName, _gameRootFolder));
                    }
                    catch (Exception exception)
                    {
                        Logger.Error($"扫描基岩版实例失败：{instanceFolder}", exception);
                    }
                }
            }
        }

        return instances;
    }

    private static bool HasClientVersionMetadata(MinecraftEntry entry)
    {
        try
        {
            using var stream = File.OpenRead(entry.ClientJsonPath);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("clientVersion", out var clientVersion) &&
                   clientVersion.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(clientVersion.GetString());
        }
        catch (IOException exception)
        {
            Logger.Warning($"读取实例版本元数据失败：{entry.ClientJsonPath}{Environment.NewLine}{exception}");
            return false;
        }
        catch (JsonException exception)
        {
            Logger.Warning($"解析实例版本元数据失败：{entry.ClientJsonPath}{Environment.NewLine}{exception}");
            return false;
        }
    }
}
