using System.Text.Json.Nodes;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Parser;
using Portal.Core.Const;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Core.Minecraft.Services;

public static class VersionModifyService
{
    private const string BackupFolderName = ".PortalModifyBackup";

    public static ManagedTask CreateModifyTask(MinecraftInstance instance, VersionManifestEntry vanilla,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        var hasLoaders = selectedEntries.Count > 0;
        var isUninstall = !hasLoaders &&
                          instance.MinecraftEntry is ModifiedMinecraftEntry { ModLoaders: { } loaders } &&
                          loaders.Any();
        return TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = isUninstall ? $"卸载实例 {instance.InstanceName} 的加载器" : $"修改实例 {instance.InstanceName}",
            Description = "正在准备修改实例版本",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消修改",
                    Description = "取消当前实例的版本修改任务",
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                }
            ]
        }, context => RunModifyAsync(context, instance, vanilla, selectedEntries, javaPath));
    }

    private static async Task RunModifyAsync(TaskExecutionContext context, MinecraftInstance instance,
        VersionManifestEntry vanilla, IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        if (instance.Type != MinecraftInstanceType.Java || instance.MinecraftEntry is not { } minecraftEntry)
            throw new InvalidOperationException("仅支持修改 Java 版实例。");

        if (!instance.CanModifyVersion)
            throw new InvalidOperationException("仅支持修改 Portal 游戏目录下的实例版本，其他格式的实例请在对应启动器中修改。");

        var dependents = FindDependentVersionIds(minecraftEntry.MinecraftFolderPath, minecraftEntry.Id);
        if (dependents.Count > 0)
            throw new InvalidOperationException(
                $"实例“{minecraftEntry.Id}”被其他版本依赖（{string.Join("、", dependents)}），" +
                "直接修改会破坏依赖它的实例。请改为修改依赖链末端的实例（如加载器实例），或先删除这些依赖版本。");

        var folderPath = minecraftEntry.MinecraftFolderPath;
        var instanceId = minecraftEntry.Id;
        var isPortalMc = MinecraftFolderLayout.TryFindPortalMcRoot(folderPath, out var portalMcRoot);
        var metadataRoot = isPortalMc ? Path.Combine(portalMcRoot, "meta") : folderPath;
        var instanceDirectory = isPortalMc
            ? instance.Layout?.InstanceRoot ?? Path.Combine(portalMcRoot, "instances", instanceId)
            : Path.Combine(folderPath, "versions", instanceId);
        var hasLoaders = selectedEntries.Count > 0;
        var usesTempBase = !isPortalMc && hasLoaders &&
                           string.Equals(instanceId, vanilla.Id, StringComparison.OrdinalIgnoreCase);
        var tempBaseId = $".portal-{instanceId}-base";
        var sourceRoots =
            MinecraftResourceRoots.ResolveForInstall(Data.ConfigEntry.MinecraftFolders, instance.FolderPath);

        try
        {
            await MinecraftInstallationTasks.RunStepAsync(context, "验证修改配置", "正在检查实例与 Java 运行时", async step =>
            {
                if (MinecraftInstallationTasks.RequiresJavaRuntime(selectedEntries.Keys) &&
                    string.IsNullOrWhiteSpace(javaPath))
                {
                    var runtime = await JavaAutoInstallCoordinator.EnsureAsync(
                        MinecraftInstallationTasks.GetRecommendedJavaVersion(vanilla.Id),
                        progress => MinecraftInstallationTasks.ReportJavaInstallProgress(step, progress),
                        step.CancellationToken);
                    javaPath = runtime?.JavaPath;
                    if (string.IsNullOrWhiteSpace(javaPath))
                        throw new InvalidOperationException("所选修改方案需要有效的 Java 运行时，修改失败。");
                }

                step.ReportProgress(1);
                await Task.CompletedTask;
            });

            if (hasLoaders)
            {
                if (isPortalMc)
                    await EnsureVanillaBaseAsync(context, metadataRoot, vanilla, sourceRoots);
                else if (usesTempBase)
                    await EnsureTempBaseAsync(context, folderPath, vanilla, instanceId, tempBaseId, sourceRoots);
                else
                    await EnsureVanillaBaseAsync(context, folderPath, vanilla, sourceRoots);
            }

            await BackupAsync(context, instanceDirectory, instanceId);

            if (hasLoaders)
            {
                if (isPortalMc)
                    await InstallLoadersPortalMcAsync(context, metadataRoot, instanceDirectory, instanceId, vanilla,
                        selectedEntries, javaPath, sourceRoots);
                else
                    await InstallLoadersAsync(context, folderPath, instanceDirectory, instanceId, vanilla,
                        selectedEntries, javaPath, usesTempBase, tempBaseId, sourceRoots);
            }
            else
            {
                if (isPortalMc)
                    await InstallPureVanillaPortalMcAsync(context, metadataRoot, instanceDirectory, instanceId, vanilla,
                        sourceRoots);
                else
                    await InstallPureVanillaAsync(context, folderPath, instanceDirectory, instanceId, vanilla,
                        sourceRoots);
            }

            if (usesTempBase)
                DeleteDirectory(Path.Combine(folderPath, "versions", tempBaseId));

            await RefreshInstancesAsync(context);
            context.SetDescription($"已完成实例“{instance.InstanceName}”的版本修改");

            DeleteDirectory(Path.Combine(instanceDirectory, BackupFolderName));
        }
        catch (Exception exception)
        {
            Logger.Error($"[VersionModify] 修改实例“{instance.InstanceName}”失败，正在恢复原版本文件。{Environment.NewLine}{exception}");
            RestoreBackup(instanceDirectory, instanceId);
            DeleteDirectory(Path.Combine(instanceDirectory, BackupFolderName));
            if (usesTempBase) DeleteDirectory(Path.Combine(folderPath, "versions", tempBaseId));
            throw;
        }
    }

    public static List<string> FindDependentVersionIds(string folderPath, string instanceId)
    {
        var dependents = new List<string>();
        var versionsDirectory = Path.Combine(folderPath, "versions");
        if (!Directory.Exists(versionsDirectory))
            return dependents;

        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory))
        {
            var versionId = Path.GetFileName(directory);
            if (string.Equals(versionId, instanceId, StringComparison.OrdinalIgnoreCase))
                continue;

            var jsonPath = Path.Combine(directory, $"{versionId}.json");
            if (!File.Exists(jsonPath))
                continue;
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
                var inheritedFrom = node?["inheritsFrom"]?.GetValue<string>();
                if (inheritedFrom is not null &&
                    string.Equals(inheritedFrom, instanceId, StringComparison.OrdinalIgnoreCase))
                    dependents.Add(versionId);
            }
            catch (Exception exception)
            {
                Logger.Debug($"[VersionModify] Failed to parse dependent check {jsonPath}: {exception}");
            }
        }

        return dependents;
    }

    private static async Task<MinecraftEntry> EnsureVanillaBaseAsync(TaskExecutionContext context, string folderPath,
        VersionManifestEntry vanilla, IEnumerable<string> sourceRoots)
    {
        var existing = TryParseVanilla(folderPath, vanilla.Id);
        if (existing is not null)
        {
            await MinecraftInstallationTasks.RunStepAsync(context, "检查原版版本", $"正在检查原版版本 {vanilla.Id} 的文件", step =>
            {
                step.SetDescription(existing.ClientJarPath is not null && File.Exists(existing.ClientJarPath)
                    ? $"原版版本 {vanilla.Id} 已存在，将复用现有文件"
                    : $"原版版本 {vanilla.Id} 文件不完整，将重新安装");
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            if (existing.ClientJarPath is not null && File.Exists(existing.ClientJarPath))
                return existing;
        }

        return await InstallVanillaBaseAsync(context, folderPath, vanilla, vanilla.Id, sourceRoots);
    }

    private static async Task EnsureTempBaseAsync(TaskExecutionContext context, string folderPath,
        VersionManifestEntry vanilla, string instanceId, string tempBaseId, IEnumerable<string> sourceRoots)
    {
        var existing = TryParseVanilla(folderPath, vanilla.Id);
        if (existing is not null && existing.ClientJarPath is not null && File.Exists(existing.ClientJarPath))
        {
            await MinecraftInstallationTasks.RunStepAsync(context, "准备原版文件",
                $"正在准备原版版本 {vanilla.Id} 的文件", step =>
                {
                    var baseDirectory = Path.Combine(folderPath, "versions", tempBaseId);
                    Directory.CreateDirectory(baseDirectory);
                    File.Copy(existing.ClientJsonPath, Path.Combine(baseDirectory, $"{tempBaseId}.json"), true);
                    File.Copy(existing.ClientJarPath, Path.Combine(baseDirectory, $"{tempBaseId}.jar"), true);
                    step.ReportProgress(1);
                    return Task.CompletedTask;
                });
            return;
        }

        await InstallVanillaBaseAsync(context, folderPath, vanilla, tempBaseId, sourceRoots);
    }

    private static VanillaMinecraftEntry? TryParseVanilla(string folderPath, string id)
    {
        try
        {
            var parsed = new MinecraftParser(folderPath).GetMinecraft(id);
            if (parsed is VanillaMinecraftEntry { Version.VersionId: var versionId } vanilla && versionId == id)
                return vanilla;
        }
        catch (Exception exception)
        {
            Logger.Debug($"[VersionModify] Failed to parse vanilla base {id}: {exception}");
        }

        return null;
    }

    private static async Task<MinecraftEntry> InstallVanillaBaseAsync(TaskExecutionContext context, string folderPath,
        VersionManifestEntry vanilla, string baseId, IEnumerable<string> sourceRoots)
    {
        return await MinecraftInstallationTasks.RunStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {vanilla.Id}",
            async step =>
            {
                var installer = VanillaInstaller.Create(folderPath, vanilla, baseId);
                installer.SourceRootDirectories = sourceRoots;
                MinecraftInstallationTasks.AttachProgressReporter(installer, step);
                return await MinecraftInstallationTasks.RunInBackgroundAsync(installer.InstallAsync,
                    step.CancellationToken);
            });
    }

    private static async Task BackupAsync(TaskExecutionContext context, string instanceDirectory, string instanceId)
    {
        await MinecraftInstallationTasks.RunStepAsync(context, "备份版本文件", "正在备份当前版本文件", step =>
        {
            var backupDirectory = Path.Combine(instanceDirectory, BackupFolderName);
            Directory.CreateDirectory(backupDirectory);
            CopyIfExists(Path.Combine(instanceDirectory, $"{instanceId}.json"),
                Path.Combine(backupDirectory, $"{instanceId}.json"));
            CopyIfExists(Path.Combine(instanceDirectory, $"{instanceId}.jar"),
                Path.Combine(backupDirectory, $"{instanceId}.jar"));
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
    }

    private static void RestoreBackup(string instanceDirectory, string instanceId)
    {
        var backupDirectory = Path.Combine(instanceDirectory, BackupFolderName);
        try
        {
            CopyIfExists(Path.Combine(backupDirectory, $"{instanceId}.json"),
                Path.Combine(instanceDirectory, $"{instanceId}.json"));
            CopyIfExists(Path.Combine(backupDirectory, $"{instanceId}.jar"),
                Path.Combine(instanceDirectory, $"{instanceId}.jar"));
        }
        catch (Exception exception)
        {
            Logger.Warning($"[VersionModify] Failed to restore backup for {instanceId}: {exception}");
        }
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[VersionModify] Failed to clean up {directory}: {exception}");
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception exception)
        {
            Logger.Warning($"[VersionModify] Failed to delete {file}: {exception}");
        }
    }

    private static async Task InstallLoadersAsync(TaskExecutionContext context, string folderPath,
        string instanceDirectory, string instanceId, VersionManifestEntry vanilla,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath, bool flatten,
        string tempBaseId,
        IEnumerable<string> sourceRoots)
    {
        await Task.Run(() =>
        {
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.json"));
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.jar"));
        });

        var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
        var primaryEntry = primary.Value;
        var primaryInstaller = primaryEntry is null
            ? null
            : CreatePrimaryInstaller(primary.Key, primaryEntry, folderPath, instanceId, javaPath, sourceRoots);
        var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
            ? CreatePreloadOptifineInstaller(folderPath, (OptifineInstallEntry)optifineEntry, javaPath)
            : null;

        var preloadTasks = new List<Task>();
        if (primaryInstaller is not null)
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, $"预加载 {primary.Key}",
                $"正在下载 {primary.Key} 安装所需的文件", step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(primaryInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(
                        token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
        if (optifineInstaller is not null)
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, "预加载 OptiFine",
                "正在下载 OptiFine 安装所需的文件", step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(optifineInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(optifineInstaller.PreloadAsync,
                        step.CancellationToken);
                }));

        await Task.WhenAll(preloadTasks);

        MinecraftEntry? minecraft = null;
        if (primaryInstaller is not null)
            minecraft = await RunInstallerStepAsync(context, $"安装 {primary.Key}",
                $"正在将 {primary.Key} 应用到当前实例", primaryInstaller);

        if (optifineInstaller is not null)
        {
            var installer = primaryInstaller is not null
                ? OptifineInstaller.Create(folderPath, (OptifineInstallEntry)optifineEntry!, minecraft)
                : OptifineInstaller.Create(folderPath, javaPath!, (OptifineInstallEntry)optifineEntry!, instanceId);
            minecraft = await RunInstallerStepAsync(context, "安装 OptiFine", "正在将 OptiFine 应用到当前实例", installer);
        }

        if (flatten)
            await FlattenAsync(context, folderPath, instanceId, vanilla.Id, tempBaseId);
    }

    private static async Task InstallPureVanillaAsync(TaskExecutionContext context, string folderPath,
        string instanceDirectory, string instanceId, VersionManifestEntry vanilla, IEnumerable<string> sourceRoots)
    {
        await Task.Run(() =>
        {
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.json"));
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.jar"));
        });

        var installer = VanillaInstaller.Create(folderPath, vanilla, instanceId);
        installer.SourceRootDirectories = sourceRoots;
        await RunInstallerStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {vanilla.Id}", installer);
    }

    private static async Task InstallPureVanillaPortalMcAsync(TaskExecutionContext context, string metadataRoot,
        string instanceDirectory, string instanceId, VersionManifestEntry vanilla, IEnumerable<string> sourceRoots)
    {
        await Task.Run(() =>
        {
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.json"));
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.jar"));
        });

        await EnsureVanillaBaseAsync(context, metadataRoot, vanilla, sourceRoots);
        await MinecraftInstallationTasks.RunStepAsync(context, "写入实例配置", "正在生成原版实例配置", step =>
        {
            MinecraftInstallationTasks.WritePortalMcMinimalInstanceJson(instanceDirectory, instanceId, vanilla.Id);
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
    }

    private static async Task InstallLoadersPortalMcAsync(TaskExecutionContext context, string metadataRoot,
        string instanceDirectory, string instanceId, VersionManifestEntry vanilla,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath,
        IEnumerable<string> sourceRoots)
    {
        var tempLoaderId = $"{instanceId}.portal-tmp";
        var loaderDirectory = Path.Combine(metadataRoot, "versions", tempLoaderId);

        await Task.Run(() =>
        {
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.json"));
            TryDeleteFile(Path.Combine(instanceDirectory, $"{instanceId}.jar"));
            DeleteDirectory(loaderDirectory);
        });

        var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
        var primaryEntry = primary.Value;
        var primaryInstaller = primaryEntry is null
            ? null
            : CreatePrimaryInstaller(primary.Key, primaryEntry, metadataRoot, tempLoaderId, javaPath, sourceRoots);
        var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
            ? CreatePreloadOptifineInstaller(metadataRoot, (OptifineInstallEntry)optifineEntry, javaPath)
            : null;

        var preloadTasks = new List<Task>();
        if (primaryInstaller is not null)
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, $"预加载 {primary.Key}",
                $"正在下载 {primary.Key} 安装所需的文件", step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(primaryInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(
                        token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
        if (optifineInstaller is not null)
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, "预加载 OptiFine",
                "正在下载 OptiFine 安装所需的文件", step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(optifineInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(optifineInstaller.PreloadAsync,
                        step.CancellationToken);
                }));

        await Task.WhenAll(preloadTasks);

        MinecraftEntry? minecraft = null;
        if (primaryInstaller is not null)
            minecraft = await RunInstallerStepAsync(context, $"安装 {primary.Key}",
                $"正在将 {primary.Key} 应用到当前实例", primaryInstaller);

        if (optifineInstaller is not null)
        {
            var installer = primaryInstaller is not null
                ? OptifineInstaller.Create(metadataRoot, (OptifineInstallEntry)optifineEntry!, minecraft)
                : OptifineInstaller.Create(metadataRoot, javaPath!, (OptifineInstallEntry)optifineEntry!,
                    tempLoaderId);
            minecraft = await RunInstallerStepAsync(context, "安装 OptiFine", "正在将 OptiFine 应用到当前实例", installer);
        }

        await MinecraftInstallationTasks.RunStepAsync(context, "整合版本文件", "正在将加载器应用到实例目录", step =>
        {
            Directory.CreateDirectory(instanceDirectory);
            MoveDirectoryContents(loaderDirectory, instanceDirectory, tempLoaderId, instanceId);
            RewriteInstanceJsonId(instanceDirectory, instanceId, tempLoaderId);
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
        DeleteDirectory(loaderDirectory);
    }

    private static void MoveDirectoryContents(string sourceDirectory, string destinationDirectory,
        string sourceId, string destinationId)
    {
        if (!Directory.Exists(sourceDirectory)) return;
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var fileName = Path.GetFileName(file);
            var targetName = fileName.Equals($"{sourceId}.json", StringComparison.OrdinalIgnoreCase)
                ? $"{destinationId}.json"
                : fileName.Equals($"{sourceId}.jar", StringComparison.OrdinalIgnoreCase)
                    ? $"{destinationId}.jar"
                    : fileName;
            File.Copy(file, Path.Combine(destinationDirectory, targetName), true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            MoveDirectoryContents(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)),
                sourceId, destinationId);
    }

    private static void RewriteInstanceJsonId(string instanceDirectory, string instanceId, string expectedSourceId)
    {
        var jsonPath = Path.Combine(instanceDirectory, $"{instanceId}.json");
        if (!File.Exists(jsonPath)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(jsonPath)) is not JsonObject node) return;
            if (node["id"]?.GetValue<string>() != expectedSourceId) return;
            node["id"] = instanceId;
            File.WriteAllText(jsonPath, node.ToJsonString());
        }
        catch (Exception exception)
        {
            Logger.Warning($"[VersionModify] Failed to rewrite instance id in {jsonPath}: {exception}");
        }
    }

    private static async Task FlattenAsync(TaskExecutionContext context, string folderPath, string instanceId,
        string vanillaId, string tempBaseId)
    {
        await MinecraftInstallationTasks.RunStepAsync(context, "整合版本文件", "正在将原版与加载器整合为独立版本", step =>
        {
            var instanceDirectory = Path.Combine(folderPath, "versions", instanceId);
            var baseDirectory = Path.Combine(folderPath, "versions", tempBaseId);
            var loaderJsonPath = Path.Combine(instanceDirectory, $"{instanceId}.json");
            var vanillaJsonPath = Path.Combine(baseDirectory, $"{tempBaseId}.json");

            var vanillaNode = JsonNode.Parse(File.ReadAllText(vanillaJsonPath)) as JsonObject
                              ?? throw new InvalidOperationException("无法解析原版版本文件。");
            var loaderNode = JsonNode.Parse(File.ReadAllText(loaderJsonPath)) as JsonObject
                             ?? throw new InvalidOperationException("无法解析加载器版本文件。");

            var merged = MergeVersionJson(vanillaNode, [loaderNode], instanceId);
            File.WriteAllText(loaderJsonPath, merged.ToJsonString());

            var hasOwnClientJar = loaderNode["downloads"] is JsonObject downloads && downloads["client"] is not null;
            var instanceJar = Path.Combine(instanceDirectory, $"{instanceId}.jar");
            var baseJar = Path.Combine(baseDirectory, $"{tempBaseId}.jar");
            if (!File.Exists(instanceJar) && !hasOwnClientJar && File.Exists(baseJar))
            {
                Directory.CreateDirectory(instanceDirectory);
                File.Copy(baseJar, instanceJar, true);
            }

            step.ReportProgress(1);
            return Task.CompletedTask;
        });

        DeleteDirectory(Path.Combine(folderPath, "versions", tempBaseId));
    }

    internal static JsonObject MergeVersionJson(JsonObject vanilla, IReadOnlyList<JsonObject> loaders,
        string instanceId)
    {
        var output = (JsonObject)vanilla.DeepClone();
        foreach (var loader in loaders)
            DeepMerge(output, loader);

        output.Remove("inheritsFrom");
        output.Remove("jar");
        output.Remove("_comment_");
        output["id"] = instanceId;
        return output;
    }

    private static void DeepMerge(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            var key = property.Key;
            if (key is "id" or "inheritsFrom" or "jar" or "_comment_" or "time" or "releaseTime")
                continue;
            var sourceValue = property.Value;
            if (sourceValue is null)
                continue;

            if (key == "minecraftArguments" && target[key] is JsonValue targetValue &&
                sourceValue is JsonValue sourceString)
            {
                var baseArgs = targetValue.GetValue<string>();
                var addedArgs = sourceString.GetValue<string>();
                target[key] = string.IsNullOrWhiteSpace(baseArgs) ? addedArgs
                    : string.IsNullOrWhiteSpace(addedArgs) ? baseArgs
                    : $"{baseArgs} {addedArgs}";
                continue;
            }

            if (target[key] is JsonObject targetObject && sourceValue is JsonObject sourceObject)
            {
                DeepMerge(targetObject, sourceObject);
                continue;
            }

            if (target[key] is JsonArray targetArray && sourceValue is JsonArray sourceArray)
            {
                foreach (var item in sourceArray)
                    targetArray.Add(item.DeepClone());
                continue;
            }

            target[key] = sourceValue.DeepClone();
        }
    }

    private static InstallerBase CreatePrimaryInstaller(LoaderKind kind, IInstallEntry entry, string folder,
        string versionId, string? javaPath, IEnumerable<string> sourceRoots)
    {
        InstallerBase installer = kind switch
        {
            LoaderKind.Forge or LoaderKind.NeoForge =>
                ForgeInstaller.Create(folder, javaPath!, (ForgeInstallEntry)entry, versionId),
            LoaderKind.Fabric => FabricInstaller.Create(folder, (FabricInstallEntry)entry, versionId),
            LoaderKind.Quilt => QuiltInstaller.Create(folder, (QuiltInstallEntry)entry, versionId),
            _ => throw new InvalidOperationException($"暂不支持在当前实例上安装 {kind}")
        };
        installer.SourceRootDirectories = sourceRoots;
        return installer;
    }

    private static OptifineInstaller CreatePreloadOptifineInstaller(string folder, OptifineInstallEntry entry,
        string? javaPath)
    {
        return OptifineInstaller.Create(folder, javaPath!, entry);
    }

    private static Task PreloadInstallerAsync(InstallerBase installer, CancellationToken cancellationToken)
    {
        return installer switch
        {
            ForgeInstaller forge => forge.PreloadAsync(cancellationToken),
            FabricInstaller fabric => fabric.PreloadAsync(cancellationToken),
            QuiltInstaller quilt => quilt.PreloadAsync(cancellationToken),
            _ => throw new NotSupportedException($"暂不支持预加载 {installer.GetType().Name} 安装器")
        };
    }

    private static async Task<MinecraftEntry> RunInstallerStepAsync(TaskExecutionContext context, string name,
        string description, InstallerBase installer)
    {
        return await MinecraftInstallationTasks.RunStepAsync(context, name, description, async step =>
        {
            MinecraftInstallationTasks.AttachProgressReporter(installer, step);
            return await MinecraftInstallationTasks.RunInBackgroundAsync(installer.InstallAsync,
                step.CancellationToken);
        });
    }

    private static async Task RefreshInstancesAsync(TaskExecutionContext context)
    {
        await MinecraftInstallationTasks.RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的实例", step =>
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
            step.SetDescription("正在刷新已安装实例");
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
    }
}