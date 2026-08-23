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
using Portal.Localization;
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
                          instance.MinecraftEntry is { Loaders.Count: > 0 };
        return TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = isUninstall
                ? string.Format(CommonLanguageManager.Instance.versionModify_uninstallTaskName.CurrentValue(), instance.InstanceName)
                : string.Format(CommonLanguageManager.Instance.versionModify_taskName.CurrentValue(), instance.InstanceName),
            Description = CommonLanguageManager.Instance.versionModify_preparing.CurrentValue(),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.versionModify_cancel.CurrentValue(),
                    Description = CommonLanguageManager.Instance.versionModify_cancelDescription.CurrentValue(),
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
            throw new InvalidOperationException(CommonLanguageManager.Instance.versionModify_onlyJava.CurrentValue());

        if (!instance.CanModifyVersion)
            throw new InvalidOperationException(CommonLanguageManager.Instance.versionModify_onlyPortal.CurrentValue());

        var folderPath = instance.Layout?.MetadataRoot ?? IridiumEntryHelper.GetMinecraftRoot(minecraftEntry);
        var dependents = FindDependentVersionIds(folderPath, minecraftEntry.Id);
        if (dependents.Count > 0)
            throw new InvalidOperationException(
                string.Format(CommonLanguageManager.Instance.versionModify_dependentsExist.CurrentValue(),
                    minecraftEntry.Id, string.Join("、", dependents)));

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
            await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_validateConfigStep.CurrentValue(),
                CommonLanguageManager.Instance.versionModify_checkingConfig.CurrentValue(), async step =>
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
                        throw new InvalidOperationException(CommonLanguageManager.Instance.versionModify_javaRuntimeRequired.CurrentValue());
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
            context.SetDescription(string.Format(CommonLanguageManager.Instance.versionModify_complete.CurrentValue(), instance.InstanceName));

            DeleteDirectory(Path.Combine(instanceDirectory, BackupFolderName));
        }
        catch (Exception exception)
        {
            Logger.Error(string.Format(LogLanguageManager.Instance.versionModify_failedRestoring.CurrentValue(), instance.InstanceName, Environment.NewLine, exception));
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
            await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_checkVanillaStep.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.versionModify_checkingVanilla.CurrentValue(), vanilla.Id), step =>
            {
                step.SetDescription(existing.ClientJarPath is not null && File.Exists(existing.ClientJarPath)
                    ? string.Format(CommonLanguageManager.Instance.versionModify_vanillaExists.CurrentValue(), vanilla.Id)
                    : string.Format(CommonLanguageManager.Instance.versionModify_vanillaIncomplete.CurrentValue(), vanilla.Id));
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
            await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_prepareVanillaStep.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.versionModify_preparingVanilla.CurrentValue(), vanilla.Id), step =>
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
        return await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.minecraft_installVanillaStep.CurrentValue(),
            string.Format(CommonLanguageManager.Instance.minecraft_installingMinecraft.CurrentValue(), vanilla.Id),
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
        await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_backupStep.CurrentValue(),
            CommonLanguageManager.Instance.versionModify_backingUp.CurrentValue(), step =>
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
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, string.Format(CommonLanguageManager.Instance.versionModify_preloadLoaderStep.CurrentValue(), primary.Key),
                string.Format(CommonLanguageManager.Instance.versionModify_downloadingLoaderFiles.CurrentValue(), primary.Key), step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(primaryInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(
                        token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
        if (optifineInstaller is not null)
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_preloadOptifineStep.CurrentValue(),
                CommonLanguageManager.Instance.versionModify_downloadingOptifineFiles.CurrentValue(), step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(optifineInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(optifineInstaller.PreloadAsync,
                        step.CancellationToken);
                }));

        await Task.WhenAll(preloadTasks);

        MinecraftEntry? minecraft = null;
        if (primaryInstaller is not null)
            minecraft = await RunInstallerStepAsync(context, string.Format(CommonLanguageManager.Instance.minecraft_installLoaderStep.CurrentValue(), primary.Key),
                string.Format(CommonLanguageManager.Instance.versionModify_applyingLoader.CurrentValue(), primary.Key), primaryInstaller);

        if (optifineInstaller is not null)
        {
            var installer = primaryInstaller is not null
                ? OptifineInstaller.Create(folderPath, (OptifineInstallEntry)optifineEntry!, minecraft)
                : OptifineInstaller.Create(folderPath, javaPath!, (OptifineInstallEntry)optifineEntry!, instanceId);
            minecraft = await RunInstallerStepAsync(context, CommonLanguageManager.Instance.minecraft_installOptifineStep.CurrentValue(), CommonLanguageManager.Instance.versionModify_applyingOptifine.CurrentValue(), installer);
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
        await RunInstallerStepAsync(context, CommonLanguageManager.Instance.minecraft_installVanillaStep.CurrentValue(),
            string.Format(CommonLanguageManager.Instance.minecraft_installingMinecraft.CurrentValue(), vanilla.Id), installer);
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
        await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_writeConfigStep.CurrentValue(),
            CommonLanguageManager.Instance.versionModify_generatingVanillaConfig.CurrentValue(), step =>
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
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, string.Format(CommonLanguageManager.Instance.versionModify_preloadLoaderStep.CurrentValue(), primary.Key),
                string.Format(CommonLanguageManager.Instance.versionModify_downloadingLoaderFiles.CurrentValue(), primary.Key), step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(primaryInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(
                        token => PreloadInstallerAsync(primaryInstaller, token), step.CancellationToken);
                }));
        if (optifineInstaller is not null)
            preloadTasks.Add(MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_preloadOptifineStep.CurrentValue(),
                CommonLanguageManager.Instance.versionModify_downloadingOptifineFiles.CurrentValue(), step =>
                {
                    MinecraftInstallationTasks.AttachProgressReporter(optifineInstaller, step);
                    return MinecraftInstallationTasks.RunInBackgroundAsync(optifineInstaller.PreloadAsync,
                        step.CancellationToken);
                }));

        await Task.WhenAll(preloadTasks);

        MinecraftEntry? minecraft = null;
        if (primaryInstaller is not null)
            minecraft = await RunInstallerStepAsync(context, string.Format(CommonLanguageManager.Instance.minecraft_installLoaderStep.CurrentValue(), primary.Key),
                string.Format(CommonLanguageManager.Instance.versionModify_applyingLoader.CurrentValue(), primary.Key), primaryInstaller);

        if (optifineInstaller is not null)
        {
            var installer = primaryInstaller is not null
                ? OptifineInstaller.Create(metadataRoot, (OptifineInstallEntry)optifineEntry!, minecraft)
                : OptifineInstaller.Create(metadataRoot, javaPath!, (OptifineInstallEntry)optifineEntry!,
                    tempLoaderId);
            minecraft = await RunInstallerStepAsync(context, CommonLanguageManager.Instance.minecraft_installOptifineStep.CurrentValue(), CommonLanguageManager.Instance.versionModify_applyingOptifine.CurrentValue(), installer);
        }

        await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_integrateStep.CurrentValue(),
            CommonLanguageManager.Instance.versionModify_integratingLoader.CurrentValue(), step =>
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
        await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.versionModify_integrateStep.CurrentValue(),
            CommonLanguageManager.Instance.versionModify_integratingStandalone.CurrentValue(), step =>
        {
            var instanceDirectory = Path.Combine(folderPath, "versions", instanceId);
            var baseDirectory = Path.Combine(folderPath, "versions", tempBaseId);
            var loaderJsonPath = Path.Combine(instanceDirectory, $"{instanceId}.json");
            var vanillaJsonPath = Path.Combine(baseDirectory, $"{tempBaseId}.json");

            var vanillaNode = JsonNode.Parse(File.ReadAllText(vanillaJsonPath)) as JsonObject
                              ?? throw new InvalidOperationException(CommonLanguageManager.Instance.versionModify_cannotParseVanilla.CurrentValue());
            var loaderNode = JsonNode.Parse(File.ReadAllText(loaderJsonPath)) as JsonObject
                             ?? throw new InvalidOperationException(CommonLanguageManager.Instance.versionModify_cannotParseLoader.CurrentValue());

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
            _ => throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.versionModify_unsupportedLoaderInstall.CurrentValue(), kind))
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
            _ => throw new NotSupportedException(string.Format(CommonLanguageManager.Instance.versionModify_unsupportedPreload.CurrentValue(), installer.GetType().Name))
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
        await MinecraftInstallationTasks.RunStepAsync(context, CommonLanguageManager.Instance.instanceRename_refreshInstancesStep.CurrentValue(),
            CommonLanguageManager.Instance.instanceRename_scanningInstances.CurrentValue(), step =>
        {
            InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
            step.SetDescription(CommonLanguageManager.Instance.instanceRename_refreshingInstances.CurrentValue());
            step.ReportProgress(1);
            return Task.CompletedTask;
        });
    }
}