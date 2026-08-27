using System.Text.Json;
using Avalonia.Threading;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Interfaces;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Installer;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Data = Portal.Core.Const.Data;

namespace Portal.Core.Minecraft;

public static class MinecraftInstallationTasks
{
    public static ManagedTask CreateInstallationTask(VersionManifestEntry vanilla, MinecraftFolderEntry folder,
        string versionId, IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        return TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = string.Format(CommonLanguageManager.Instance.minecraft_installJavaTaskName.CurrentValue(), versionId),
            Description = CommonLanguageManager.Instance.minecraft_creatingInstallTask.CurrentValue(),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.minecraft_cancelInstall.CurrentValue(),
                    Description = CommonLanguageManager.Instance.minecraft_cancelInstallDescription.CurrentValue(),
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
        }, context => RunInstallationAsync(context, vanilla, folder, versionId, selectedEntries, javaPath));
    }

    public static bool RequiresJavaRuntime(IEnumerable<LoaderKind> kinds)
    {
        return kinds.Any(kind => kind is LoaderKind.Forge or LoaderKind.NeoForge or LoaderKind.OptiFine);
    }

    private static async Task RunInstallationAsync(TaskExecutionContext context, VersionManifestEntry vanilla,
        MinecraftFolderEntry folder, string versionId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        if (folder.DetectedLayout.Kind == MinecraftFolderKind.PortalMc)
        {
            await RunPortalMcInstallationAsync(context, vanilla, folder, versionId, selectedEntries, javaPath);
            return;
        }

        await RunTraditionalInstallationAsync(context, vanilla, folder, versionId, selectedEntries, javaPath);
    }

    private static async Task RunPortalMcInstallationAsync(TaskExecutionContext context, VersionManifestEntry vanilla,
        MinecraftFolderEntry folder, string versionId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        var metaRoot = Path.Combine(folder.FolderPath, "meta");
        var instancesRoot = Path.Combine(folder.FolderPath, "instances");
        var hasLoaders = selectedEntries.Count > 0;
        var sourceRoots =
            MinecraftResourceRoots.ResolveForInstall(Data.ConfigEntry.MinecraftFolders, folder.FolderPath);

        await RunStepAsync(context, CommonLanguageManager.Instance.minecraft_validateInstallConfig.CurrentValue(),
            CommonLanguageManager.Instance.minecraft_checkingInstallConfig.CurrentValue(), async step =>
        {
            if (RequiresJavaRuntime(selectedEntries.Keys) && string.IsNullOrWhiteSpace(javaPath))
            {
                var runtime = await JavaAutoInstallCoordinator.EnsureAsync(GetRecommendedJavaVersion(vanilla.Id),
                    progress => ReportJavaInstallProgress(step, progress), step.CancellationToken);
                javaPath = runtime?.JavaPath;
                if (string.IsNullOrWhiteSpace(javaPath))
                    throw new InvalidOperationException(CommonLanguageManager.Instance.minecraft_javaRuntimeRequired.CurrentValue());
            }

            if (Directory.Exists(Path.Combine(instancesRoot, versionId)))
                throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.minecraft_instanceIdExists.CurrentValue(), versionId));
            step.ReportProgress(1);
            await Task.CompletedTask;
        });

        var instanceDirectory = Path.Combine(instancesRoot, versionId);

        var vanillaId = hasLoaders ? vanilla.Id : versionId;
        var vanillaDirectory = Path.Combine(metaRoot, "versions", vanillaId);
        var vanillaDirectoryExisted = Directory.Exists(vanillaDirectory);

        var effectiveLoaderId = hasLoaders && versionId.Equals(vanilla.Id, StringComparison.OrdinalIgnoreCase)
            ? $"{versionId}.portal-tmp"
            : versionId;
        try
        {
            var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
            var primaryEntry = primary.Value;
            var primaryInstaller = primaryEntry is null
                ? null
                : CreatePrimaryInstaller(primary.Key, primaryEntry, metaRoot, effectiveLoaderId, javaPath, sourceRoots);
            var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
                ? CreatePreloadOptifineInstaller(metaRoot, (OptifineInstallEntry)optifineEntry, javaPath)
                : null;

            var vanillaTask = RunStepAsync(context, CommonLanguageManager.Instance.minecraft_installVanillaStep.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.minecraft_installingMinecraft.CurrentValue(), vanilla.Id), async step =>
            {
                var installer = VanillaInstaller.Create(metaRoot, vanilla, vanillaId);
                installer.SourceRootDirectories = sourceRoots;
                AttachProgressReporter(installer, step);
                return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
            });
            var preloadTasks = new List<Task>();
            if (primaryInstaller is not null)
                preloadTasks.Add(RunStepAsync(context, string.Format(CommonLanguageManager.Instance.minecraft_preloadLoaderStep.CurrentValue(), primary.Key),
                    string.Format(CommonLanguageManager.Instance.minecraft_parallelDownloading.CurrentValue(), primary.Key), step =>
                {
                    AttachProgressReporter(primaryInstaller, step);
                    return RunInBackgroundAsync(token => PreloadInstallerAsync(primaryInstaller, token),
                        step.CancellationToken);
                }));
            if (optifineInstaller is not null)
                preloadTasks.Add(RunStepAsync(context, CommonLanguageManager.Instance.minecraft_preloadOptifineStep.CurrentValue(),
                    CommonLanguageManager.Instance.minecraft_parallelDownloadingOptifine.CurrentValue(), step =>
                {
                    AttachProgressReporter(optifineInstaller, step);
                    return RunInBackgroundAsync(optifineInstaller.PreloadAsync, step.CancellationToken);
                }));

            await Task.WhenAll([vanillaTask, .. preloadTasks]);
            var minecraft = await vanillaTask;
            if (primaryInstaller is not null)
                minecraft = await RunInstallerStepAsync(context, string.Format(CommonLanguageManager.Instance.minecraft_installLoaderStep.CurrentValue(), primary.Key),
                    string.Format(CommonLanguageManager.Instance.minecraft_installingLatestLoader.CurrentValue(), primary.Key),
                    primaryInstaller);

            if (optifineInstaller is not null)
            {
                var installer = primaryInstaller is not null
                    ? OptifineInstaller.Create(metaRoot, (OptifineInstallEntry)optifineEntry!, minecraft)
                    : OptifineInstaller.Create(metaRoot, javaPath!, (OptifineInstallEntry)optifineEntry!,
                        effectiveLoaderId);
                minecraft = await RunInstallerStepAsync(context, CommonLanguageManager.Instance.minecraft_installOptifineStep.CurrentValue(),
                    CommonLanguageManager.Instance.minecraft_installingLatestOptifine.CurrentValue(), installer);
            }

            await RunStepAsync(context, CommonLanguageManager.Instance.minecraft_createInstanceStep.CurrentValue(),
                CommonLanguageManager.Instance.minecraft_generatingInstanceConfig.CurrentValue(), step =>
            {
                Directory.CreateDirectory(instancesRoot);
                if (hasLoaders)
                {
                    var loaderVersionDirectory = Path.Combine(metaRoot, "versions", effectiveLoaderId);
                    if (Directory.Exists(loaderVersionDirectory))
                    {
                        Directory.Move(loaderVersionDirectory, instanceDirectory);
                        if (!effectiveLoaderId.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                        {
                            var jsonFile = Path.Combine(instanceDirectory, $"{effectiveLoaderId}.json");
                            if (File.Exists(jsonFile))
                                File.Move(jsonFile, Path.Combine(instanceDirectory, $"{versionId}.json"));
                            var jarFile = Path.Combine(instanceDirectory, $"{effectiveLoaderId}.jar");
                            if (File.Exists(jarFile))
                                File.Move(jarFile, Path.Combine(instanceDirectory, $"{versionId}.jar"));
                        }
                    }
                }
                else
                {
                    WritePortalMcMinimalInstanceJson(instanceDirectory, versionId, vanillaId);
                }

                step.ReportProgress(1);
                return Task.CompletedTask;
            });

            await RunStepAsync(context, CommonLanguageManager.Instance.minecraft_refreshInstancesStep.CurrentValue(),
                CommonLanguageManager.Instance.minecraft_scanningNewInstances.CurrentValue(), step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription(string.Format(CommonLanguageManager.Instance.minecraft_instancesRefreshed.CurrentValue(), minecraft.Id));
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription(string.Format(CommonLanguageManager.Instance.minecraft_installationComplete.CurrentValue(), minecraft.Id));
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[MinecraftInstall] Installation {versionId} was cancelled: {exception}");
            await DeleteVersionDirectoryAsync(instanceDirectory);
            if (hasLoaders && !effectiveLoaderId.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                await DeleteVersionDirectoryAsync(Path.Combine(metaRoot, "versions", effectiveLoaderId));
            if (!vanillaDirectoryExisted && !hasLoaders)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteVersionDirectoryAsync(instanceDirectory);
            if (hasLoaders && !effectiveLoaderId.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                await DeleteVersionDirectoryAsync(Path.Combine(metaRoot, "versions", effectiveLoaderId));
            if (!vanillaDirectoryExisted && !hasLoaders)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
    }

    public static void WritePortalMcMinimalInstanceJson(string instanceDirectory, string instanceId, string vanillaId)
    {
        Directory.CreateDirectory(instanceDirectory);
        var jsonPath = Path.Combine(instanceDirectory, $"{instanceId}.json");
        using var stream = File.Create(jsonPath);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("id", instanceId);
        writer.WriteString("inheritsFrom", vanillaId);
        writer.WriteString("mainClass", "net.minecraft.client.main.Main");
        writer.WritePropertyName("libraries");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static async Task RunTraditionalInstallationAsync(TaskExecutionContext context,
        VersionManifestEntry vanilla,
        MinecraftFolderEntry folder, string versionId,
        IReadOnlyDictionary<LoaderKind, IInstallEntry> selectedEntries, string? javaPath)
    {
        await RunStepAsync(context, CommonLanguageManager.Instance.minecraft_validateInstallConfig.CurrentValue(),
            CommonLanguageManager.Instance.minecraft_checkingInstallConfig.CurrentValue(), async step =>
        {
            if (RequiresJavaRuntime(selectedEntries.Keys) && string.IsNullOrWhiteSpace(javaPath))
            {
                var runtime = await JavaAutoInstallCoordinator.EnsureAsync(GetRecommendedJavaVersion(vanilla.Id),
                    progress => ReportJavaInstallProgress(step, progress), step.CancellationToken);
                javaPath = runtime?.JavaPath;
                if (string.IsNullOrWhiteSpace(javaPath))
                    throw new InvalidOperationException(CommonLanguageManager.Instance.minecraft_javaRuntimeRequired.CurrentValue());
            }

            if (Directory.Exists(Path.Combine(folder.FolderPath, "versions", versionId)))
                throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.minecraft_instanceIdExists.CurrentValue(), versionId));
            step.ReportProgress(1);
            await Task.CompletedTask;
        });

        var versionDirectory = Path.Combine(folder.FolderPath, "versions", versionId);

        var vanillaId = selectedEntries.Count > 0 ? vanilla.Id : versionId;
        var vanillaDirectory = Path.Combine(folder.FolderPath, "versions", vanillaId);
        var vanillaDirectoryExisted = Directory.Exists(vanillaDirectory);
        var sourceRoots =
            MinecraftResourceRoots.ResolveForInstall(Data.ConfigEntry.MinecraftFolders, folder.FolderPath);
        try
        {
            var primary = selectedEntries.FirstOrDefault(x => x.Key != LoaderKind.OptiFine);
            var primaryEntry = primary.Value;
            var primaryInstaller = primaryEntry is null
                ? null
                : CreatePrimaryInstaller(primary.Key, primaryEntry, folder.FolderPath, versionId, javaPath,
                    sourceRoots);
            var optifineInstaller = selectedEntries.TryGetValue(LoaderKind.OptiFine, out var optifineEntry)
                ? CreatePreloadOptifineInstaller(folder.FolderPath, (OptifineInstallEntry)optifineEntry, javaPath)
                : null;

            var vanillaTask = RunStepAsync(context, CommonLanguageManager.Instance.minecraft_installVanillaStep.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.minecraft_installingMinecraft.CurrentValue(), vanilla.Id), async step =>
            {
                var installer = VanillaInstaller.Create(folder.FolderPath, vanilla, vanillaId);
                installer.SourceRootDirectories = sourceRoots;
                AttachProgressReporter(installer, step);
                return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
            });
            var preloadTasks = new List<Task>();
            if (primaryInstaller is not null)
                preloadTasks.Add(RunStepAsync(context, string.Format(CommonLanguageManager.Instance.minecraft_preloadLoaderStep.CurrentValue(), primary.Key),
                    string.Format(CommonLanguageManager.Instance.minecraft_parallelDownloading.CurrentValue(), primary.Key), step =>
                {
                    AttachProgressReporter(primaryInstaller, step);
                    return RunInBackgroundAsync(token => PreloadInstallerAsync(primaryInstaller, token),
                        step.CancellationToken);
                }));
            if (optifineInstaller is not null)
                preloadTasks.Add(RunStepAsync(context, CommonLanguageManager.Instance.minecraft_preloadOptifineStep.CurrentValue(),
                    CommonLanguageManager.Instance.minecraft_parallelDownloadingOptifine.CurrentValue(), step =>
                {
                    AttachProgressReporter(optifineInstaller, step);
                    return RunInBackgroundAsync(optifineInstaller.PreloadAsync, step.CancellationToken);
                }));

            await Task.WhenAll([vanillaTask, .. preloadTasks]);
            var minecraft = await vanillaTask;
            if (primaryInstaller is not null)
                minecraft = await RunInstallerStepAsync(context, string.Format(CommonLanguageManager.Instance.minecraft_installLoaderStep.CurrentValue(), primary.Key),
                    string.Format(CommonLanguageManager.Instance.minecraft_installingLatestLoader.CurrentValue(), primary.Key),
                    primaryInstaller);

            if (optifineInstaller is not null)
            {
                var installer = primaryInstaller is not null
                    ? OptifineInstaller.Create(folder.FolderPath, (OptifineInstallEntry)optifineEntry!, minecraft)
                    : OptifineInstaller.Create(folder.FolderPath, javaPath!, (OptifineInstallEntry)optifineEntry!,
                        versionId);
                minecraft = await RunInstallerStepAsync(context, CommonLanguageManager.Instance.minecraft_installOptifineStep.CurrentValue(),
                    CommonLanguageManager.Instance.minecraft_installingLatestOptifine.CurrentValue(), installer);
            }

            await RunStepAsync(context, CommonLanguageManager.Instance.minecraft_refreshInstancesStep.CurrentValue(),
                CommonLanguageManager.Instance.minecraft_scanningNewInstances.CurrentValue(), step =>
            {
                InstanceManager.Instance.RefreshAll(Data.ConfigEntry.MinecraftFolders);
                step.SetDescription(string.Format(CommonLanguageManager.Instance.minecraft_instancesRefreshed.CurrentValue(), minecraft.Id));
                step.ReportProgress(1);
                return Task.CompletedTask;
            });
            context.SetDescription(string.Format(CommonLanguageManager.Instance.minecraft_installationComplete.CurrentValue(), minecraft.Id));
        }
        catch (OperationCanceledException exception)
        {
            Logger.Debug($"[MinecraftInstall] Installation {versionId} was cancelled: {exception}");
            await DeleteVersionDirectoryAsync(versionDirectory);
            if (!vanillaDirectoryExisted && vanillaDirectory != versionDirectory)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Error(exception);
            await DeleteVersionDirectoryAsync(versionDirectory);
            if (!vanillaDirectoryExisted && vanillaDirectory != versionDirectory)
                await DeleteVersionDirectoryAsync(vanillaDirectory);
            throw;
        }
    }

    private static Task DeleteVersionDirectoryAsync(string directory)
    {
        return Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (Exception exception)
            {
                Logger.Warning($"[MinecraftInstall] Failed to clean up {directory}: {exception}");
            }
        });
    }

    private static InstallerBase CreatePrimaryInstaller(LoaderKind kind, IInstallEntry entry, string folder,
        string versionId,
        string? javaPath, IEnumerable<string> sourceRoots)
    {
        InstallerBase installer = kind switch
        {
            LoaderKind.Forge or LoaderKind.NeoForge =>
                ForgeInstaller.Create(folder, javaPath!, (ForgeInstallEntry)entry, versionId),
            LoaderKind.Fabric => FabricInstaller.Create(folder, (FabricInstallEntry)entry, versionId),
            LoaderKind.Quilt => QuiltInstaller.Create(folder, (QuiltInstallEntry)entry, versionId),
            _ => throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.minecraft_unsupportedLoader.CurrentValue(), kind))
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
            _ => throw new NotSupportedException(string.Format(CommonLanguageManager.Instance.minecraft_unsupportedPreloadLoader.CurrentValue(), installer.GetType().Name))
        };
    }

    private static async Task<MinecraftEntry> RunInstallerStepAsync(TaskExecutionContext context, string name,
        string description,
        InstallerBase installer)
    {
        return await RunStepAsync(context, name, description, async step =>
        {
            AttachProgressReporter(installer, step);
            return await RunInBackgroundAsync(installer.InstallAsync, step.CancellationToken);
        });
    }

    public static Task RunInBackgroundAsync(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }

    public static Task<T> RunInBackgroundAsync<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }

    public static void AttachProgressReporter(InstallerBase installer, TaskExecutionContext context)
    {
        installer.ProgressChanged += CreateProgressReporter(context);
    }

    private static EventHandler<InstallProgressChangedEventArgs> CreateProgressReporter(TaskExecutionContext context)
    {
        InstallProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        return (_, progress) =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;

            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is { } current)
                    ReportInstallerProgress(context, current);
            }, DispatcherPriority.Background);
        };
    }

    public static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 },
            operation);
        step.Start();
        await step.Completion;
        if (step.Exception is not null) throw new InvalidOperationException(step.Exception.Message, step.Exception);
        context.CancellationToken.ThrowIfCancellationRequested();
    }

    public static async Task<T> RunStepAsync<T>(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task<T>> operation)
    {
        T? result = default;
        await RunStepAsync(context, name, description, async step => { result = await operation(step); });
        return result!;
    }

    private static void ReportInstallerProgress(TaskExecutionContext context, InstallProgressChangedEventArgs progress)
    {
        context.ReportProgress(progress.Progress);
        var count = progress.TotalStepTaskCount > 0
            ? $" {progress.FinishedStepTaskCount}/{progress.TotalStepTaskCount}"
            : string.Empty;
        var speed = progress.IsStepSupportSpeed && progress.Speed >= 0
            ? string.Format(CommonLanguageManager.Instance.minecraft_speedSuffix.CurrentValue(), FormatDownloadSpeed(progress.Speed))
            : string.Empty;
        context.SetDescription($"{GetInstallStepDescription(progress.StepName)}{count}{speed}");
    }

    public static void ReportJavaInstallProgress(TaskExecutionContext context, JavaInstallProgress progress)
    {
        if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
            try
            {
                context.ReportProgress(progress.Fraction);
                context.SetDescription(progress.SpeedBytesPerSecond > 0
                    ? $"{progress.Stage}{string.Format(CommonLanguageManager.Instance.minecraft_javaInstallSpeed.CurrentValue(), DefaultDownloader.FormatSize(progress.SpeedBytesPerSecond, true))}"
                    : progress.Stage);
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private static string GetInstallStepDescription(InstallStep step)
    {
        return step switch
        {
            InstallStep.Started => CommonLanguageManager.Instance.minecraft_stepPreparingInstaller.CurrentValue(),
            InstallStep.DownloadVersionJson => CommonLanguageManager.Instance.minecraft_stepDownloadingVersionJson.CurrentValue(),
            InstallStep.ParseMinecraft => CommonLanguageManager.Instance.minecraft_stepParsingMinecraft.CurrentValue(),
            InstallStep.DownloadAssetIndexFile => CommonLanguageManager.Instance.minecraft_stepDownloadingAssetIndex.CurrentValue(),
            InstallStep.DownloadLibraries => CommonLanguageManager.Instance.minecraft_stepDownloadingLibraries.CurrentValue(),
            InstallStep.CopyLibraries => CommonLanguageManager.Instance.minecraft_stepCopyingLibraries.CurrentValue(),
            InstallStep.DownloadPackage => CommonLanguageManager.Instance.minecraft_stepDownloadingPackage.CurrentValue(),
            InstallStep.ParsePackage => CommonLanguageManager.Instance.minecraft_stepParsingPackage.CurrentValue(),
            InstallStep.WriteVersionJsonAndSomeDependencies => CommonLanguageManager.Instance.minecraft_stepWritingVersionConfig.CurrentValue(),
            InstallStep.RunInstallProcessor => CommonLanguageManager.Instance.minecraft_stepRunningInstallProcessor.CurrentValue(),
            InstallStep.RanToCompletion => CommonLanguageManager.Instance.minecraft_stepInstallComplete.CurrentValue(),
            InstallStep.Interrupted => CommonLanguageManager.Instance.minecraft_stepInstallInterrupted.CurrentValue(),
            _ => CommonLanguageManager.Instance.minecraft_stepInstallingGameFiles.CurrentValue()
        };
    }

    private static string FormatDownloadSpeed(double bytesPerSecond)
    {
        string[] units = ["B/s", "KiB/s", "MiB/s", "GiB/s"];
        var value = bytesPerSecond;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    public static string GetLoaderVersion(LoaderKind kind, IInstallEntry entry)
    {
        return kind switch
        {
            LoaderKind.Quilt => ((QuiltInstallEntry)entry).Loader.Version,
            _ => entry.DisplayVersion
        };
    }

    public static string? GetJavaPath(int? majorVersion = null)
    {
        if (majorVersion is { } requiredVersion)
        {
            if (Data.ConfigEntry.JavaVersionDefaultPaths.TryGetValue(requiredVersion, out var defaultPath) &&
                Data.ConfigEntry.JavaRuntimes.FirstOrDefault(runtime =>
                    runtime.MajorVersion == requiredVersion &&
                    string.Equals(runtime.JavaPath, defaultPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(runtime.JavaPath)) is { } defaultRuntime)
                return defaultRuntime.JavaPath;

            return Data.ConfigEntry.JavaRuntimes
                .Where(runtime => runtime.MajorVersion == requiredVersion)
                .Select(runtime => runtime.JavaPath)
                .FirstOrDefault(File.Exists);
        }

        foreach (var defaultPath in Data.ConfigEntry.JavaVersionDefaultPaths.Values)
            if (Data.ConfigEntry.JavaRuntimes.FirstOrDefault(runtime =>
                    string.Equals(runtime.JavaPath, defaultPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(runtime.JavaPath)) is { } defaultRuntime)
                return defaultRuntime.JavaPath;

        return Data.ConfigEntry.JavaRuntimes.Select(runtime => runtime.JavaPath).FirstOrDefault(File.Exists);
    }

    public static int GetRecommendedJavaVersion(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.', '-', '_');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) ||
            major != 1)
            return 21;
        if (minor >= 21) return 21;
        if (minor == 20 && parts.Length > 2 && int.TryParse(parts[2], out var patch) && patch >= 5) return 21;
        if (minor >= 18) return 17;
        if (minor == 17) return 16;
        return 8;
    }
}
