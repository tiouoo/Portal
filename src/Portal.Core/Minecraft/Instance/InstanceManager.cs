using System.Collections.ObjectModel;
using MinecraftLaunch.Components.Parser;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;

namespace Portal.Core.Minecraft.Instance;

public class InstanceManager
{
    private static InstanceManager? _instance;

    public static InstanceManager Instance
    {
        get { return _instance ??= new InstanceManager(); }
    }

    public ObservableCollection<MinecraftInstance> Instances { get; } = [];

    public List<string> VersionFolders { get; } = new() { "versions", "bedrock_versions" };

    /// <summary>
    /// 当实例统计数据发生变化时触发的事件
    /// </summary>
    public event EventHandler? StatisticsChanged;

    private InstanceManager() { }

    /// <summary>
    /// 通知统计数据已更新
    /// </summary>
    public void NotifyStatisticsChanged()
    {
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshAll(IEnumerable<(string FolderPath, string FolderName)> folders)
    {
        Instances.Clear();

        foreach (var (folderPath, folderName) in folders)
        {
            if (!Directory.Exists(folderPath)) continue;

            var scanner = new FolderScanner(folderPath, folderName, VersionFolders);
            var instances = scanner.Scan();
            foreach (var instance in instances)
            {
                Instances.Add(instance);
            }
        }
        
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
        var javaEntries = minecraftParser.GetMinecrafts().ToDictionary(e => e.Id);

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
                else if (instanceType == MinecraftInstanceType.Bedrock)
                {
                    try
                    {
                        var bedrockConfig = BedrockHelper.GetInstanceConfig(instanceFolder);
                        instances.Add(new MinecraftInstance(bedrockConfig, _folderName, _gameRootFolder));
                    }
                    catch
                    {
                    }
                }
            }
        }

        return instances;
    }
}
