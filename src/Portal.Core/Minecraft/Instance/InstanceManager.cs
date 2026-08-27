using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Iridium.Models.Minecraft;
using Iridium.Minecraft;
using Iridium.Minecraft.Formats;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Instance;

public class InstanceManager
{
    private const int MaxScanConcurrency = 2;
    private static InstanceManager? _instance;

    private InstanceManager()
    {
        if (OperatingSystem.IsWindows() ||
            (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64))
            VersionFolders.Add("bedrock_versions");
    }

    public static InstanceManager Instance
    {
        get { return _instance ??= new InstanceManager(); }
    }

    public ObservableCollection<MinecraftInstance> Instances { get; } = new InstanceCollection();

    public List<string> VersionFolders { get; } = new() { "versions" };

    public event EventHandler? InstancesChanged;

    public event EventHandler? StatisticsChanged;

    public event EventHandler<MinecraftInstance>? InstanceIconChanged;

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
        => Task.Run(() => ScanAllAsync(folders)).GetAwaiter().GetResult();

    public static async Task<List<MinecraftInstance>> ScanAllAsync(IEnumerable<MinecraftFolderEntry> folders,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var folderList = folders.ToArray();
        using var gate = new SemaphoreSlim(MaxScanConcurrency);
        var scans = folderList.Select(async folder =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ScanFolderAsync(folder, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        var result = (await Task.WhenAll(scans).ConfigureAwait(false)).SelectMany(items => items).ToList();

        Logger.Info(string.Format(LogLanguageManager.Instance.instanceManager_scanComplete.CurrentValue(), result.Count, stopwatch.ElapsedMilliseconds));
        return result;
    }

    private static async Task<IReadOnlyList<MinecraftInstance>> ScanFolderAsync(MinecraftFolderEntry folder,
        CancellationToken cancellationToken)
    {
        var folderPath = folder.FolderPath;
        if (!Directory.Exists(folderPath))
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.instanceManager_folderMissing.CurrentValue(), folderPath));
            return [];
        }

        Logger.Info(string.Format(LogLanguageManager.Instance.instanceManager_folderScanStart.CurrentValue(), folderPath));
        var layout = folder.DetectedLayout;
        return layout.Kind == MinecraftFolderKind.Standard
            ? await new FolderScanner(layout.RootPath, folder.FolderName, Instance.VersionFolders)
                .ScanAsync(cancellationToken).ConfigureAwait(false)
            : await ExternalMinecraftScanner.ScanAsync(folder, cancellationToken).ConfigureAwait(false);
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
                try
                {
                    var config = BedrockHelper.GetInstanceConfig(instanceFolder);
                    result.Add(new MinecraftInstance(config, folder.FolderName, layout.RootPath));
                }
                catch (Exception exception)
                {
                    Logger.Error(string.Format(LogLanguageManager.Instance.instanceManager_bedrockScanFailed.CurrentValue(), instanceFolder), exception);
                }
        }

        return result;
    }

    public void ApplyInstances(IEnumerable<MinecraftInstance> instances)
    {
        var loadedInstances = instances as ICollection<MinecraftInstance> ?? instances.ToList();
        Logger.Info(string.Format(LogLanguageManager.Instance.instanceManager_applyStart.CurrentValue(), loadedInstances.Count));
        ((InstanceCollection)Instances).ReplaceWith(loadedInstances);

        InstancesChanged?.Invoke(this, EventArgs.Empty);
        NotifyStatisticsChanged();
    }

    private sealed class InstanceCollection : ObservableCollection<MinecraftInstance>
    {
        public void ReplaceWith(IEnumerable<MinecraftInstance> items)
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
            OnPropertyChanged(new(nameof(Count)));
            OnPropertyChanged(new("Item[]"));
            OnCollectionChanged(new(NotifyCollectionChangedAction.Reset));
        }
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
    private readonly string _folderName;
    private readonly string _gameRootFolder;
    private readonly List<string> _versionFolders;

    public FolderScanner(string gameRootFolder, string folderName, List<string> versionFolders)
    {
        _gameRootFolder = gameRootFolder;
        _folderName = folderName;
        _versionFolders = versionFolders;
    }

    public async Task<List<MinecraftInstance>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var instances = new List<MinecraftInstance>();

        var provider = new MinecraftProvider(new DirectoryInfo(_gameRootFolder),
            [new StandardMinecraftProvider()]);
        var parsedContexts = await provider.GetMinecraftsAsync(cancellationToken);
        var parsedJavaEntries = parsedContexts.Select(context => context.Entry).ToList();
        var inheritedIds = parsedJavaEntries
            .Select(entry => entry.InheritsFrom)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var internalBaseIds = parsedJavaEntries
            .Where(entry => inheritedIds.Contains(entry.Id) &&
                            HasClientVersionMetadata(parsedContexts.First(context => context.Entry == entry)))
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var javaContexts = parsedContexts
            .Where(context => !internalBaseIds.Contains(context.Entry.Id))
            .ToDictionary(context => context.Entry.Id);

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
                cancellationToken.ThrowIfCancellationRequested();
                var folderKey = Path.GetFullPath(instanceFolder);
                if (processedFolders.Contains(folderKey))
                    continue;
                processedFolders.Add(folderKey);

                var instanceType = InstanceManager.GetInstanceType(instanceFolder);

                if (instanceType == MinecraftInstanceType.Java)
                    try
                    {
                        var folderName = Path.GetFileName(instanceFolder);
                        if (javaContexts.TryGetValue(folderName, out var minecraftContext))
                            instances.Add(new MinecraftInstance(minecraftContext)
                            {
                                FolderName = _folderName,
                                FolderPath = _gameRootFolder
                            });
                    }
                    catch (Exception e)
                    {
                        Logger.Error(string.Format(LogLanguageManager.Instance.instanceManager_javaScanFailed.CurrentValue(), instanceFolder), e);
                    }
                else if (instanceType == MinecraftInstanceType.Bedrock)
                    try
                    {
                        var bedrockConfig = BedrockHelper.GetInstanceConfig(instanceFolder);
                        instances.Add(new MinecraftInstance(bedrockConfig, _folderName, _gameRootFolder));
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(string.Format(LogLanguageManager.Instance.instanceManager_bedrockScanFailed.CurrentValue(), instanceFolder), exception);
                    }
            }
        }

        return instances;
    }

    private static bool HasClientVersionMetadata(MinecraftContext context)
    {
        var entry = context.Entry;
        try
        {
            using var stream = File.OpenRead(IridiumEntryHelper.GetLayout(context).GetVersionJsonPath(entry));
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("clientVersion", out var clientVersion) &&
                   clientVersion.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(clientVersion.GetString());
        }
        catch (IOException exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.instanceManager_readVersionMetadataFailed.CurrentValue(), IridiumEntryHelper.GetLayout(context).GetVersionJsonPath(entry), Environment.NewLine + exception));
            return false;
        }
        catch (JsonException exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.instanceManager_parseVersionMetadataFailed.CurrentValue(), IridiumEntryHelper.GetLayout(context).GetVersionJsonPath(entry), Environment.NewLine + exception));
            return false;
        }
    }
}
