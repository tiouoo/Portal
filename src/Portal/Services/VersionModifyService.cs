using System.Text.Json.Nodes;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;
using MinecraftLaunch.Components.Parser;
using Portal.Const;
using Portal.Core.Helpers;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Operations.Java;
using Portal.Views.Pages.DownloadPages;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Services;

/// <summary>
/// 修改已安装 Java 实例的游戏版本/加载器版本，保留存档、模组、资源包等用户数据。
/// 修改过程中会备份版本文件，失败时自动恢复。
/// </summary>
public static class VersionModifyService
{
    private const string BackupFolderName = ".PortalModifyBackup";

    public static ManagedTask CreateModifyTask(MinecraftInstance instance, VersionManifestEntry vanilla,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath) =>
        TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"修改实例 {instance.InstanceName}",
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

    private static async Task RunModifyAsync(TaskExecutionContext context, MinecraftInstance instance,
        VersionManifestEntry vanilla, IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        if (instance.Type != MinecraftInstanceType.Java || instance.MinecraftEntry is not { } minecraftEntry)
            throw new InvalidOperationException("仅支持修改 Java 版实例。");

        // 若其他版本（如加载器基座）通过 inheritsFrom 依赖本实例，直接修改会破坏它们。
        // 例如安装 1.21.6 的 Forge 时会顺带安装 1.21.6 原版基座，若把 1.21.6 改成别的
        // 版本，依赖它的 Forge 版本就再也无法启动。此时应让用户改为修改依赖链末端的实例。
        var dependents = FindDependentVersionIds(minecraftEntry.MinecraftFolderPath, minecraftEntry.Id);
        if (dependents.Count > 0)
        {
            throw new InvalidOperationException(
                $"实例“{minecraftEntry.Id}”被其他版本依赖（{string.Join("、", dependents)}），" +
                "直接修改会破坏依赖它的实例。请改为修改依赖链末端的实例（如加载器实例），或先删除这些依赖版本。");
        }

        var folderPath = minecraftEntry.MinecraftFolderPath;
        var instanceId = minecraftEntry.Id;
        var versionDirectory = Path.Combine(folderPath, "versions", instanceId);
        var hasLoaders = selectedEntries.Count > 0;
        var usesTempBase = hasLoaders && string.Equals(instanceId, vanilla.Id, StringComparison.OrdinalIgnoreCase);
        var tempBaseId = $".portal-{instanceId}-base";

        try
        {
            await MinecraftInstallationViewModel.RunStepAsync(context, "验证修改配置", "正在检查实例与 Java 运行时", async step =>
            {
                if (MinecraftInstallationViewModel.RequiresJavaRuntime(selectedEntries.Keys) &&
                    string.IsNullOrWhiteSpace(javaPath))
                {
                    var runtime = await JavaAutoInstallCoordinator.EnsureAsync(
                        MinecraftInstallationViewModel.GetRecommendedJavaVersion(vanilla.Id),
                        progress => MinecraftInstallationViewModel.ReportJavaInstallProgress(step, progress),
                        step.CancellationToken);
                    javaPath = runtime?.JavaPath;
                    if (string.IsNullOrWhiteSpace(javaPath))
                        throw new InvalidOperationException("所选修改方案需要有效的 Java 运行时，修改失败。");
                }
                step.ReportProgress(1);
                await Task.CompletedTask;
            });

            // 安装/校验原版基座
            if (hasLoaders)
            {
                if (usesTempBase)
                    await EnsureTempBaseAsync(context, folderPath, vanilla, instanceId, tempBaseId);
                else
                    await EnsureVanillaBaseAsync(context, folderPath, vanilla);
            }

            await BackupAsync(context, versionDirectory, instanceId);

            if (hasLoaders)
            {
                await InstallLoadersAsync(context, folderPath, instanceId, vanilla, selectedEntries,
                    javaPath, usesTempBase, tempBaseId);
            }
            else
            {
                await InstallPureVanillaAsync(context, folderPath, instanceId, vanilla);
            }

            if (usesTempBase)
                DeleteDirectory(Path.Combine(folderPath, "versions", tempBaseId));

            await RefreshInstancesAsync(context);
            context.SetDescription($"已完成实例“{instance.InstanceName}”的版本修改");

            DeleteDirectory(Path.Combine(versionDirectory, BackupFolderName));
        }
        catch (Exception exception)
        {
            Logger.Error($"[VersionModify] 修改实例“{instance.InstanceName}”失败，正在恢复原版本文件。{Environment.NewLine}{exception}");
            RestoreBackup(versionDirectory, instanceId);
            DeleteDirectory(Path.Combine(versionDirectory, BackupFolderName));
            if (usesTempBase) DeleteDirectory(Path.Combine(folderPath, "versions", tempBaseId));
            throw;
        }
    }

    /// <summary>
    /// 查找通过 inheritsFrom 依赖指定实例 id 的其他版本目录（如加载器基座）。
    /// 修改被依赖的实例会破坏这些版本，需在修改前阻止。
    /// </summary>
    internal static List<string> FindDependentVersionIds(string folderPath, string instanceId)
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

    /// <summary>
    /// 校验并复用已存在的原版基座，文件缺失时重新安装。
    /// </summary>
    private static async Task<MinecraftEntry> EnsureVanillaBaseAsync(TaskExecutionContext context, string folderPath,
        VersionManifestEntry vanilla)
    {
        var existing = TryParseVanilla(folderPath, vanilla.Id);
        if (existing is not null)
        {
            await MinecraftInstallationViewModel.RunStepAsync(context, "检查原版版本", $"正在检查原版版本 {vanilla.Id} 的文件", step =>
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

        return await InstallVanillaBaseAsync(context, folderPath, vanilla, vanilla.Id);
    }

    /// <summary>
    /// 确保同版本修改所需的临时原版基座存在。优先复制实例自身的原版 json 与 jar，
    /// 避免同版本加载器修改时重复下载整个客户端；文件不完整时才重新下载。
    /// </summary>
    private static async Task EnsureTempBaseAsync(TaskExecutionContext context, string folderPath,
        VersionManifestEntry vanilla, string instanceId, string tempBaseId)
    {
        var existing = TryParseVanilla(folderPath, vanilla.Id);
        if (existing is not null && existing.ClientJarPath is not null && File.Exists(existing.ClientJarPath))
        {
            await MinecraftInstallationViewModel.RunStepAsync(context, "准备原版文件",
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

        await InstallVanillaBaseAsync(context, folderPath, vanilla, tempBaseId);
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
        VersionManifestEntry vanilla, string baseId) =>
        await MinecraftInstallationViewModel.RunStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {vanilla.Id}", async step =>
        {
            var installer = VanillaInstaller.Create(folderPath, vanilla, baseId);
            MinecraftInstallationViewModel.AttachProgressReporter(installer, step);
            return await MinecraftInstallationViewModel.RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
        });

    private static async Task BackupAsync(TaskExecutionContext context, string versionDirectory, string instanceId)
    {
        await MinecraftInstallationViewModel.RunStepAsync(context, "备份版本文件", "正在备份当前版本文件", step =>
        {
            var backupDirectory = Path.Combine(versionDirectory, BackupFolderName);
            Directory.CreateDirectory(backupDirectory);
            CopyIfExists(Path.Combine(versionDirectory, $"{instanceId}.json"),
                Path.Combine(backupDirectory, $"{instanceId}.json"));
            CopyIfExists(Path.Combine(versionDirectory, $"{instanceId}.jar"),
                Path.Combine(backupDirectory, $"{instanceId}.jar"));
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
    }

    private static void RestoreBackup(string versionDirectory, string instanceId)
    {
        var backupDirectory = Path.Combine(versionDirectory, BackupFolderName);
        try
        {
            CopyIfExists(Path.Combine(backupDirectory, $"{instanceId}.json"),
                Path.Combine(versionDirectory, $"{instanceId}.json"));
            CopyIfExists(Path.Combine(backupDirectory, $"{instanceId}.jar"),
                Path.Combine(versionDirectory, $"{instanceId}.jar"));
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

    private static async Task InstallLoadersAsync(TaskExecutionContext context, string folderPath, string instanceId,
        VersionManifestEntry vanilla,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath, bool flatten, string tempBaseId)
    {
        // 移除旧版本文件，避免 Fabric/Quilt 安装器复用旧配置文件
        var versionDirectory = Path.Combine(folderPath, "versions", instanceId);
        await Task.Run(() =>
        {
            TryDeleteFile(Path.Combine(versionDirectory, $"{instanceId}.json"));
            TryDeleteFile(Path.Combine(versionDirectory, $"{instanceId}.jar"));
        });

        var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
        var primaryEntry = primary.Value;
        var primaryInstaller = primaryEntry is null
            ? null
            : CreatePrimaryInstaller(primary.Key, primaryEntry, folderPath, instanceId, javaPath);
        var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
            ? CreatePreloadOptifineInstaller(folderPath, (OptifineInstallEntry)optifineEntry, javaPath)
            : null;

        var preloadTasks = new List<Task>();
        if (primaryInstaller is not null)
        {
            preloadTasks.Add(MinecraftInstallationViewModel.RunStepAsync(context, $"预加载 {primary.Key}",
                $"正在下载 {primary.Key} 安装所需的文件", step =>
                {
                    MinecraftInstallationViewModel.AttachProgressReporter(primaryInstaller, step);
                    return MinecraftInstallationViewModel.RunInBackgroundAsync(
                        token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
        }
        if (optifineInstaller is not null)
        {
            preloadTasks.Add(MinecraftInstallationViewModel.RunStepAsync(context, "预加载 OptiFine",
                "正在下载 OptiFine 安装所需的文件", step =>
                {
                    MinecraftInstallationViewModel.AttachProgressReporter(optifineInstaller, step);
                    return MinecraftInstallationViewModel.RunInBackgroundAsync(optifineInstaller.PreloadAsync,
                        step.CancellationToken);
                }));
        }

        await Task.WhenAll(preloadTasks);

        MinecraftEntry? minecraft = null;
        if (primaryInstaller is not null)
        {
            minecraft = await RunInstallerStepAsync(context, $"安装 {primary.Key}",
                $"正在将 {primary.Key} 应用到当前实例", primaryInstaller);
        }

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

    private static async Task InstallPureVanillaAsync(TaskExecutionContext context, string folderPath, string instanceId,
        VersionManifestEntry vanilla)
    {
        var versionDirectory = Path.Combine(folderPath, "versions", instanceId);
        await Task.Run(() =>
        {
            TryDeleteFile(Path.Combine(versionDirectory, $"{instanceId}.json"));
            TryDeleteFile(Path.Combine(versionDirectory, $"{instanceId}.jar"));
        });

        await RunInstallerStepAsync(context, "安装原版 Minecraft", $"正在安装 Minecraft {vanilla.Id}",
            VanillaInstaller.Create(folderPath, vanilla, instanceId));
    }

    /// <summary>
    /// 当实例 id 与原版 id 相同（如直接修改“1.20.1”为加载器版本）时，
    /// 将继承结构整合为独立的版本文件：json 合并 + 客户端 jar 处理。
    /// </summary>
    private static async Task FlattenAsync(TaskExecutionContext context, string folderPath, string instanceId,
        string vanillaId, string tempBaseId)
    {
        await MinecraftInstallationViewModel.RunStepAsync(context, "整合版本文件", "正在将原版与加载器整合为独立版本", step =>
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

            // 仅当加载器不提供自己的客户端 jar（如旧版 Forge）时，才复用原版客户端 jar
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

    internal static JsonObject MergeVersionJson(JsonObject vanilla, IReadOnlyList<JsonObject> loaders, string instanceId)
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

            // 旧版本使用 minecraftArguments 而非 arguments，启动参数需要拼接而不是覆盖
            if (key == "minecraftArguments" && target[key] is JsonValue targetValue && sourceValue is JsonValue sourceString)
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
        string versionId, string? javaPath) =>
        kind switch
        {
            LoaderKind.Forge or LoaderKind.NeoForge =>
                ForgeInstaller.Create(folder, javaPath!, (ForgeInstallEntry)entry, versionId),
            LoaderKind.Fabric => FabricInstaller.Create(folder, (FabricInstallEntry)entry, versionId),
            LoaderKind.Quilt => QuiltInstaller.Create(folder, (QuiltInstallEntry)entry, versionId),
            _ => throw new InvalidOperationException($"暂不支持在当前实例上安装 {kind}")
        };

    private static OptifineInstaller CreatePreloadOptifineInstaller(string folder, OptifineInstallEntry entry,
        string? javaPath) => OptifineInstaller.Create(folder, javaPath!, entry);

    private static Task PreloadInstallerAsync(InstallerBase installer, CancellationToken cancellationToken) =>
        installer switch
        {
            ForgeInstaller forge => forge.PreloadAsync(cancellationToken),
            FabricInstaller fabric => fabric.PreloadAsync(cancellationToken),
            QuiltInstaller quilt => quilt.PreloadAsync(cancellationToken),
            _ => throw new NotSupportedException($"暂不支持预加载 {installer.GetType().Name} 安装器")
        };

    private static async Task<MinecraftEntry> RunInstallerStepAsync(TaskExecutionContext context, string name,
        string description, InstallerBase installer) =>
        await MinecraftInstallationViewModel.RunStepAsync(context, name, description, async step =>
        {
            MinecraftInstallationViewModel.AttachProgressReporter(installer, step);
            return await MinecraftInstallationViewModel.RunInBackgroundAsync(installer.InstallAsync,
                step.CancellationToken);
        });

    private static async Task RefreshInstancesAsync(TaskExecutionContext context) =>
        await MinecraftInstallationViewModel.RunStepAsync(context, "刷新已安装实例", "正在扫描安装目录中的实例", step =>
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
            step.SetDescription("正在刷新已安装实例");
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
}
