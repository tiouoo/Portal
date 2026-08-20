using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Launch;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Graphics;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Minecraft.Services;
using Portal.Core.Services.SystemResources;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Classes;
using ModLoaderType = MinecraftLaunch.Base.Enums.ModLoaderType;

namespace Portal.Core.Minecraft;

public static class MinecraftLaunchService
{
    public static Func<BedrockInstanceConfig, IBedrockLaunch>? DefaultBedrockLauncherFactory { get; set; }

    public static Action? OpenJavaDownloadPage { get; set; }

    public static Task LaunchAsync(MinecraftInstance instance, TopLevel? topLevel, MinecraftLaunchOptions options,
        RecentPlayTarget? target = null)
    {
        topLevel?.Notice(string.Format(CommonLanguageManager.Instance.launch_starting.CurrentValue(), instance.InstanceName));
        var launchCompleted = false;
        Process? process = null;
        var logSession = new MinecraftLogSession(instance);
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = string.Format(CommonLanguageManager.Instance.launch_taskName.CurrentValue(), GetEditionName(instance), instance.InstanceName),
            Description = CommonLanguageManager.Instance.launch_preparing.CurrentValue(),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.launch_cancelLaunch.CurrentValue(),
                    Description = CommonLanguageManager.Instance.launch_cancelLaunchDescription.CurrentValue(),
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !launchCompleted && !managedTask.IsTerminal
                },
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.launch_killProcess.CurrentValue(),
                    Description = CommonLanguageManager.Instance.launch_killProcessDescription.CurrentValue(),
                    ExecuteAsync = (_, _) =>
                    {
                        if (process == null)
                            throw new InvalidOperationException(CommonLanguageManager.Instance.launch_processUnavailable.CurrentValue());
                        if (!process.HasExited)
                            process.Kill(true);
                        return Task.CompletedTask;
                    },
                    IsVisible = managedTask => launchCompleted && !managedTask.IsTerminal &&
                                               process != null && IsProcessRunning(process)
                },
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.launch_viewLog.CurrentValue(),
                    Description = CommonLanguageManager.Instance.launch_viewLogDescription.CurrentValue(),
                    ExecuteAsync = (_, _) =>
                    {
                        options.OpenLog?.Invoke(logSession!);
                        return Task.CompletedTask;
                    },
                    IsVisible = _ => options.OpenLog != null
                }
            ]
        });
        ManagedTask? verifyAccount = null;
        ManagedTask? selectJava = null;
        ManagedTask? buildArguments = null;
        ManagedTask? completeResources = null;
        if (instance.Type == MinecraftInstanceType.Java)
        {
            verifyAccount = task.CreateChild(new TaskOptions
            {
                Name = CommonLanguageManager.Instance.launch_verifyAccount.CurrentValue(), Description = CommonLanguageManager.Instance.launch_verifyAccountWaiting.CurrentValue(), Progress = 0
            });
            selectJava = task.CreateChild(new TaskOptions
            {
                Name = CommonLanguageManager.Instance.launch_selectJava.CurrentValue(), Description = CommonLanguageManager.Instance.launch_selectJavaWaiting.CurrentValue(), Progress = 0
            });
            buildArguments = task.CreateChild(new TaskOptions
            {
                Name = CommonLanguageManager.Instance.launch_buildArguments.CurrentValue(), Description = CommonLanguageManager.Instance.launch_buildArgumentsWaiting.CurrentValue(), Progress = 0
            });
            completeResources = task.CreateChild(new TaskOptions
            {
                Name = CommonLanguageManager.Instance.launch_completeResources.CurrentValue(), Description = CommonLanguageManager.Instance.launch_completeResourcesWaiting.CurrentValue(), Progress = 0
            });
        }

        var startGame = task.CreateChild(new TaskOptions
        {
            Name = instance.Type == MinecraftInstanceType.Bedrock ? CommonLanguageManager.Instance.launch_startBedrock.CurrentValue() : CommonLanguageManager.Instance.launch_startMinecraft.CurrentValue(),
            Description = instance.Type == MinecraftInstanceType.Bedrock ? CommonLanguageManager.Instance.launch_startGameWaiting.CurrentValue() : CommonLanguageManager.Instance.launch_completeResourcesWaiting.CurrentValue(),
            Progress = 0
        });
        task.Start();
        RunWorkflowAsync(instance, topLevel, options, target, task, verifyAccount, selectJava, buildArguments,
            completeResources,
            startGame, logSession,
            launchedProcess =>
            {
                process = launchedProcess;
                launchCompleted = true;
                task.RefreshActions();
            }).ContinueWith(
            completedTask => Logger.Error(string.Format(LogLanguageManager.Instance.launch_workflowFaulted.CurrentValue(), instance.InstanceName), completedTask.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task.Completion;
    }

    private static async Task RunWorkflowAsync(MinecraftInstance instance, TopLevel? topLevel,
        MinecraftLaunchOptions options,
        RecentPlayTarget? target, ManagedTask task,
        ManagedTask? verifyAccount, ManagedTask? selectJava, ManagedTask? buildArguments,
        ManagedTask? completeResources,
        ManagedTask startGame,
        MinecraftLogSession logSession, Action<Process> processStarted)
    {
        try
        {
            if (instance.Type == MinecraftInstanceType.Bedrock)
            {
                startGame.Start(context => LaunchBedrockAsync(context, instance, topLevel, options, task, logSession,
                    processStarted));
                await startGame.Completion;
                ThrowIfFailed(startGame);
                return;
            }

            if (instance.Type != MinecraftInstanceType.Java || instance.MinecraftEntry == null)
                throw new InvalidOperationException(CommonLanguageManager.Instance.launch_onlyJavaSupported.CurrentValue());

            Account? account = null;
            JavaEntry? java = null;
            LaunchConfig? config = null;
            Dictionary<string, string>? placeholders = null;
            IReadOnlyDictionary<string, string>? highPerformanceGpuEnvironment = null;

            verifyAccount!.Start(async context =>
            {
                context.SetRunning(CommonLanguageManager.Instance.launch_verifyingAccount.CurrentValue());
                account = await VerifyAccountAsync(options);
                context.ReportProgress(1);
            });
            await verifyAccount.Completion;
            ThrowIfFailed(verifyAccount);

            selectJava!.Start(async context =>
            {
                context.SetRunning(CommonLanguageManager.Instance.launch_checkingJavaRuntime.CurrentValue());
                java = await SelectJavaAsync(instance, options, context, context.CancellationToken);
                if (options.AutoSetJavaHighPerformanceGpu)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        HighPerformanceGpuService.TrySetWindowsHighPerformanceGpuPreference(java.JavaPath);
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        context.SetDescription(CommonLanguageManager.Instance.launch_resolvingHighPerformanceGpu.CurrentValue());
                        highPerformanceGpuEnvironment =
                            await HighPerformanceGpuService.ResolveLinuxHighPerformanceGpuEnvironmentAsync();
                    }
                }

                context.ReportProgress(1);
            });
            await selectJava.Completion;
            ThrowIfFailed(selectJava);

            buildArguments!.Start(context =>
            {
                context.SetRunning(CommonLanguageManager.Instance.launch_applyingGameSettings.CurrentValue());
                placeholders = LaunchCustomization.BuildPlaceholders(instance, account, java, options);
                config = CreateLaunchConfig(instance, account!, java!, options, target, placeholders);
                if (highPerformanceGpuEnvironment is { Count: > 0 } gpuEnvironment)
                {
                    var merged = new Dictionary<string, string>(gpuEnvironment);
                    foreach (var (key, value) in config.EnvironmentVariables)
                        merged[key] = value;
                    config.EnvironmentVariables = merged;
                }

                context.ReportProgress(1);
                return Task.CompletedTask;
            });
            await buildArguments.Completion;
            ThrowIfFailed(buildArguments);

            completeResources!.Start(context => CompleteResourcesAsync(context, instance.MinecraftEntry!, options));
            await completeResources.Completion;
            ThrowIfFailed(completeResources);

            startGame.Start(context => StartGameStepAsync(context, instance, config!, topLevel, task, logSession!,
                options, placeholders!, processStarted));
            await startGame.Completion;
            ThrowIfFailed(startGame);
        }
        catch (OperationCanceledException) when (task.IsCancellationRequested)
        {
            if (!task.IsTerminal)
                task.Complete();
            Notice(topLevel, CommonLanguageManager.Instance.launch_taskCancelled.CurrentValue(), NotificationType.Information);
        }
        catch (MissingJavaVersionException exception)
        {
            if (!task.IsTerminal)
                task.Fault(exception);
            NoticeMissingJava(topLevel, exception);
        }
        catch (Exception exception)
        {
            if (!task.IsTerminal)
                task.Fault(exception);
            Notice(topLevel, string.Format(CommonLanguageManager.Instance.launch_failed.CurrentValue(), GetFailureReason(exception)), NotificationType.Error);
        }
    }

    private static void ThrowIfFailed(ManagedTask task)
    {
        if (task.Exception != null)
        {
            if (task.Exception is MissingJavaVersionException missingJavaVersion)
                throw missingJavaVersion;
            throw new InvalidOperationException(task.Exception.Message, task.Exception);
        }
        task.CancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task StartGameStepAsync(TaskExecutionContext context, MinecraftInstance instance,
        LaunchConfig config, TopLevel? topLevel, ManagedTask task, MinecraftLogSession logSession,
        MinecraftLaunchOptions options, Dictionary<string, string> placeholders, Action<Process> processStarted)
    {
        await RunBeforeLaunchCommandAsync(context, topLevel, options, placeholders);
        if (options.AutoOptimizeMemoryBeforeGameLaunch && OperatingSystem.IsWindows())
        {
            context.SetRunning(CommonLanguageManager.Instance.launch_optimizingMemory.CurrentValue());
            try
            {
                await MemoryOptimizationService.OptimizeAsync(context.CancellationToken);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                Logger.Warning(string.Format(LogLanguageManager.Instance.launch_memoryOptimizationFailed.CurrentValue(), Environment.NewLine, exception));
                Notice(topLevel, string.Format(CommonLanguageManager.Instance.launch_memoryOptimizationIncomplete.CurrentValue(), exception.Message), NotificationType.Warning);
            }
        }

        context.SetRunning(CommonLanguageManager.Instance.launch_startingProcess.CurrentValue());
        await PrepareGraphicsBeforeLaunchAsync(instance, config, context.CancellationToken);
        if (options.SetChineseLanguageOnLaunch && instance.MinecraftEntry != null)
        {
            context.SetRunning(CommonLanguageManager.Instance.launch_settingGameLanguage.CurrentValue());
            var entry = instance.MinecraftEntry;
            GameOptionsService.SetChineseLanguage(entry.ToWorkingPath(config.IsEnableIndependency), entry.ReleaseTime);
        }

        WriteStartupLog(logSession, instance, config);
        var mcProcess = await Task.Run(async () =>
        {
            var parser = new MinecraftParser(instance.MinecraftEntry!.MinecraftFolderPath);
            return await new MinecraftRunner(config, parser)
                .RunAsync(instance.MinecraftEntry, context.CancellationToken);
        }, context.CancellationToken);
        if (mcProcess == null)
            throw new InvalidOperationException(CommonLanguageManager.Instance.launch_noProcessInfo.CurrentValue());
        ObserveProcess(instance, topLevel, mcProcess, task, context, logSession, options);
        processStarted(mcProcess.Process);
        OnGameProcessStarted(mcProcess.Process, options, placeholders, true, instance);
        context.ReportProgress(1);
    }

    private static async Task PrepareGraphicsBeforeLaunchAsync(MinecraftInstance instance, LaunchConfig config,
        CancellationToken cancellationToken)
    {
        var entry = instance.MinecraftEntry;
        if (entry == null || instance.JavaConfig == null)
            return;

        var versionId = entry is ModifiedMinecraftEntry { HasInheritance: true } modified
            ? modified.InheritedMinecraft.Version.VersionId
            : entry.Version.VersionId;
        var graphics = instance.JavaConfig.GraphicsBackend;
        var version = GameVersion.Parse(versionId ?? entry.Id);
        var effective = GraphicsEnvironmentResolver.Resolve(graphics,
            instance.JavaConfig.OpenGlRenderer, instance.JavaConfig.VulkanRenderer, version);

        if (effective.Renderer is not { MesaDriverName: { } mesaDriverName })
            return;

        var platform = Renderers.CurrentPlatform;
        if (platform.Os != OperatingSystemKind.Windows)
            return;

        var nativeDir = Path.Combine(entry.NativesDirectoryPath ??
                                     Path.Combine(entry.MinecraftFolderPath, "versions", entry.Id, "natives"),
            "mesa-loader");
        Directory.CreateDirectory(nativeDir);

        var jarPath = await MesaLoaderService.EnsureMesaLoaderAsync(cancellationToken);

        var agent = GraphicsLaunchArgumentsBuilder.BuildJavaAgent(jarPath, mesaDriverName);
        config.JvmArguments = config.JvmArguments.Append("-javaagent:" + agent);
    }

    private static async Task RunBeforeLaunchCommandAsync(TaskExecutionContext context, TopLevel? topLevel,
        MinecraftLaunchOptions options, Dictionary<string, string> placeholders)
    {
        var command = LaunchCustomization.Apply(options.BeforeLaunchCommand, placeholders);
        if (string.IsNullOrWhiteSpace(command))
            return;

        context.SetRunning(CommonLanguageManager.Instance.launch_runningBeforeLaunchCommand.CurrentValue());
        var exitCode = await LaunchCustomization.RunShellCommandAsync(command,
            placeholders.GetValueOrDefault("{game_dir}"), context.CancellationToken);
        if (exitCode != 0)
            Notice(topLevel, string.Format(CommonLanguageManager.Instance.launch_beforeLaunchCommandExitCode.CurrentValue(), exitCode), NotificationType.Warning);
    }

    private static void OnGameProcessStarted(Process process, MinecraftLaunchOptions options,
        Dictionary<string, string> placeholders, bool overrideWindowTitle, MinecraftInstance instance)
    {
        placeholders["{process_id}"] = process.Id.ToString();

        if (OperatingSystem.IsWindows() && options.EnableGameOverlay && options.ShowGameOverlay != null)
            Dispatcher.UIThread.Post(() => options.ShowGameOverlay(process, instance));

        if (overrideWindowTitle && placeholders.GetValueOrDefault("{title}") is { Length: > 0 } title)
            LaunchCustomization.WatchWindowTitle(process, title);

        var command = LaunchCustomization.Apply(options.AfterLaunchCommand, placeholders);
        if (!string.IsNullOrWhiteSpace(command))
            LaunchCustomization.RunShellCommandDetached(command, placeholders.GetValueOrDefault("{game_dir}"));

        if (options.GameStarted != null)
            Dispatcher.UIThread.Post(options.GameStarted);
    }

    private static async Task CompleteResourcesAsync(TaskExecutionContext context, MinecraftEntry entry,
        MinecraftLaunchOptions options)
    {
        context.SetRunning(CommonLanguageManager.Instance.launch_checkingResources.CurrentValue());
        var downloader = new MinecraftResourceDownloader(entry)
        {
            SourceRootDirectories = options.ResourceSourceRoots
        };


        await RunStepAsync(context, CommonLanguageManager.Instance.launch_checkResourceFilesStep.CurrentValue(),
            CommonLanguageManager.Instance.launch_verifyingResourceIntegrity.CurrentValue(), async step =>
        {
            await Task.Factory.StartNew(
                    () => downloader.VerifyDependenciesAsync(2,
                        step.CancellationToken),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();

            var copyCount = downloader.CopyItems.Count;
            var downloadCount = downloader.DependenciesToDownload.Count;
            step.SetDescription(copyCount + downloadCount == 0
                ? CommonLanguageManager.Instance.launch_resourcesReady.CurrentValue()
                : string.Format(CommonLanguageManager.Instance.launch_resourcesToProcess.CurrentValue(), copyCount, downloadCount));
            step.ReportProgress(1);
        });


        if (downloader.CopyItems.Count > 0)
            await RunStepAsync(context, CommonLanguageManager.Instance.launch_copyResourcesStep.CurrentValue(),
                CommonLanguageManager.Instance.launch_copyingResources.CurrentValue(), step =>
            {
                AttachCopyProgressReporter(step, downloader);
                return Task.Run(() => downloader.CopyDependencies(4,
                    step.CancellationToken), step.CancellationToken);
            });


        if (downloader.DependenciesToDownload.Count > 0)
            await RunStepAsync(context, CommonLanguageManager.Instance.launch_downloadResourcesStep.CurrentValue(),
                CommonLanguageManager.Instance.launch_downloadingResources.CurrentValue(), async step =>
            {
                AttachDownloadProgressReporter(step, downloader);
                var result = await Task.Factory.StartNew(
                        () => downloader.DownloadDependenciesAsync(step.CancellationToken),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default)
                    .Unwrap();
                if (result.Failed.Any())
                    throw new IOException(string.Format(CommonLanguageManager.Instance.launch_resourceCompletionFailed.CurrentValue(), result.Failed.Count()));
                step.ReportProgress(1);
            });

        context.ReportProgress(1);
        context.SetDescription(CommonLanguageManager.Instance.launch_resourcesCompleted.CurrentValue());
    }

    private static void AttachCopyProgressReporter(TaskExecutionContext context, MinecraftResourceDownloader downloader)
    {
        ResourceCopyProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        downloader.CopyProgressChanged += (_, progress) =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is not { } current) return;

                var completion = current.TotalBytes > 0
                    ? Math.Clamp((double)current.CopiedBytes / current.TotalBytes, 0, 1)
                    : (double?)null;
                context.ReportProgress(completion);
                context.SetDescription(FormatCopyProgress(current));
            }, DispatcherPriority.Background);
        };
    }

    private static void AttachDownloadProgressReporter(TaskExecutionContext context,
        MinecraftResourceDownloader downloader)
    {
        ResourceDownloadProgressChangedEventArgs? latestProgress = null;
        var dispatchQueued = 0;
        downloader.ProgressChanged += (_, progress) =>
        {
            if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;

            Volatile.Write(ref latestProgress, progress);
            if (Interlocked.Exchange(ref dispatchQueued, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dispatchQueued, 0);
                if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                if (Volatile.Read(ref latestProgress) is not { } current) return;

                var completion = current.TotalBytes > 0
                    ? Math.Clamp((double)current.DownloadedBytes / current.TotalBytes, 0, 1)
                    : (double?)null;
                context.ReportProgress(completion);
                context.SetDescription(FormatResourceProgress(current.CompletedCount, current.TotalCount,
                    current.DownloadedBytes, current.TotalBytes, current.Speed, current.EstimatedRemaining));
            }, DispatcherPriority.Background);
        };
    }

    private static string FormatCopyProgress(ResourceCopyProgressChangedEventArgs progress)
    {
        var files = progress.TotalCount > 0 ? string.Format(CommonLanguageManager.Instance.launch_copyFilesCount.CurrentValue(), progress.CompletedCount, progress.TotalCount) : CommonLanguageManager.Instance.launch_copyPreparing.CurrentValue();
        var transferred = progress.TotalBytes > 0
            ? string.Format(CommonLanguageManager.Instance.launch_copyTransferred.CurrentValue(), DefaultDownloader.FormatSize(progress.CopiedBytes), DefaultDownloader.FormatSize(progress.TotalBytes))
            : string.Empty;
        var currentFile = !string.IsNullOrWhiteSpace(progress.CurrentFile)
            ? string.Format(CommonLanguageManager.Instance.launch_copyCurrentFile.CurrentValue(), Path.GetFileName(progress.CurrentFile))
            : string.Empty;
        return string.Format(CommonLanguageManager.Instance.launch_copyingResourcesFormat.CurrentValue(), files, transferred, currentFile);
    }

    private static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
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

    private static string FormatResourceProgress(int completedCount, int totalCount, long downloadedBytes,
        long totalBytes,
        double speed, TimeSpan estimatedRemaining)
    {
        var files = totalCount > 0 ? string.Format(CommonLanguageManager.Instance.launch_copyFilesCount.CurrentValue(), completedCount, totalCount) : CommonLanguageManager.Instance.launch_downloadPreparing.CurrentValue();
        var transferred = totalBytes > 0
            ? string.Format(CommonLanguageManager.Instance.launch_copyTransferred.CurrentValue(), DefaultDownloader.FormatSize(downloadedBytes), DefaultDownloader.FormatSize(totalBytes))
            : string.Empty;
        var speedText = speed > 0 ? string.Format(CommonLanguageManager.Instance.launch_speedSuffix.CurrentValue(), DefaultDownloader.FormatSize(speed, true)) : string.Empty;

        return string.Format(CommonLanguageManager.Instance.launch_completingResourcesFormat.CurrentValue(), files, transferred, speedText);
    }

    private static async Task<Account> VerifyAccountAsync(MinecraftLaunchOptions options)
    {
        var account = options.Account
                      ?? throw new InvalidOperationException(CommonLanguageManager.Instance.launch_selectAccountFirst.CurrentValue());
        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException(CommonLanguageManager.Instance.launch_accountNoPlayerName.CurrentValue());

        switch (account.AccountType)
        {
            case AccountType.Offline:
                return new OfflineAccount(account.Name,
                    account.Uuid ?? MinecraftAccount.GetMinecraftOfflineUuid(account.Name),
                    account.AccessToken ?? Guid.NewGuid().ToString("N"));
            case AccountType.Yggdrasil:
                if (!account.Uuid.HasValue || string.IsNullOrWhiteSpace(account.AccessToken) ||
                    string.IsNullOrWhiteSpace(account.ClientToken) ||
                    string.IsNullOrWhiteSpace(account.YggdrasilServerUrl))
                    throw new InvalidOperationException(CommonLanguageManager.Instance.launch_yggdrasilIncomplete.CurrentValue());
                return new YggdrasilAccount(account.Name, account.Uuid.Value, account.AccessToken, account.ClientToken,
                    account.YggdrasilServerUrl) { MetaData = account.MetaData };
            case AccountType.Microsoft:
                var refreshed = await AccountRefresher.RefreshMicrosoft(account)
                                ?? throw new InvalidOperationException(CommonLanguageManager.Instance.launch_microsoftRefreshFailed.CurrentValue());
                options.AccountRefreshed?.Invoke(account, refreshed);
                if (!refreshed.Uuid.HasValue || string.IsNullOrWhiteSpace(refreshed.AccessToken) ||
                    string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                    throw new InvalidOperationException(CommonLanguageManager.Instance.launch_microsoftMissingInfo.CurrentValue());
                return new MicrosoftAccount(refreshed.Name, refreshed.Uuid.Value, refreshed.AccessToken,
                    refreshed.RefreshToken, refreshed.LastRefreshTime ?? DateTime.Now);
            default:
                throw new InvalidOperationException(CommonLanguageManager.Instance.launch_unsupportedAccountType.CurrentValue());
        }
    }

    private static async Task<JavaEntry> SelectJavaAsync(MinecraftInstance instance, MinecraftLaunchOptions options,
        TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var javaConfig = instance.JavaConfig
                         ?? throw new InvalidOperationException(CommonLanguageManager.Instance.launch_javaConfigMissing.CurrentValue());
        var preferred = javaConfig.EnableSpecificJava ? javaConfig.SpecificJavaEntry : null;
        var requiredVersion = instance.MinecraftEntry!.GetAppropriateJavaVersion();

        if (preferred is not null)
        {
            var selected = await SelectViableJavaAsync(instance, preferred, [preferred], cancellationToken);
            if (selected is not null) return selected;
            throw new MissingJavaVersionException(requiredVersion);
        }

        var candidates = options.JavaRuntimes
            .Where(runtime => runtime.MajorVersion == requiredVersion)
            .ToList();
        MoveDefaultJavaFirst(candidates, options.JavaVersionDefaults, requiredVersion);

        if (candidates.Count == 0)
        {
            candidates = (await JavaRuntimeManager.ScanAsync(cancellationToken))
                .Where(runtime => runtime.MajorVersion == requiredVersion)
                .ToList();
            MoveDefaultJavaFirst(candidates, options.JavaVersionDefaults, requiredVersion);
        }

        foreach (var candidate in candidates)
        {
            if (await JavaRuntimeVerifier.IsUsableAsync(candidate.JavaPath, candidate.MajorVersion, cancellationToken))
                return ToJavaEntry(candidate);
        }

        throw new MissingJavaVersionException(requiredVersion);
    }

    private static void MoveDefaultJavaFirst(List<JavaRuntimeEntry> candidates,
        IReadOnlyDictionary<int, string> javaVersionDefaults, int majorVersion)
    {
        if (!javaVersionDefaults.TryGetValue(majorVersion, out var defaultPath) ||
            string.IsNullOrWhiteSpace(defaultPath))
            return;

        var match = candidates.FirstOrDefault(runtime =>
            string.Equals(runtime.JavaPath, defaultPath, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return;

        candidates.Remove(match);
        candidates.Insert(0, match);
    }

    private static async Task<JavaEntry?> SelectViableJavaAsync(MinecraftInstance instance, JavaRuntimeEntry? preferred,
        IReadOnlyList<JavaRuntimeEntry> candidates, CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return null;
        var javaEntries = candidates.Select(ToJavaEntry).ToList();
        var ordered = preferred != null
            ? javaEntries
            : OrderJavaCandidates(instance.MinecraftEntry!, javaEntries);

        foreach (var candidate in ordered)
        {
            if (!await JavaRuntimeVerifier.IsUsableAsync(candidate.JavaPath, candidate.MajorVersion, cancellationToken))
                continue;
            return candidate;
        }

        return null;
    }

    private static List<JavaEntry> OrderJavaCandidates(MinecraftEntry minecraft, IReadOnlyList<JavaEntry> javaEntries)
    {
        var requiredVersion = minecraft.GetAppropriateJavaVersion();
        var requiresExactVersion = minecraft is ModifiedMinecraftEntry modified &&
                                   modified.ModLoaders.Any(loader =>
                                       loader.Type is ModLoaderType.Forge or ModLoaderType.NeoForge);

        var compatible = javaEntries.Where(IsCompatible).ToList();
        var incompatible = javaEntries.Where(candidate => !IsCompatible(candidate)).ToList();
        compatible.Sort((a, b) => a.MajorVersion.CompareTo(b.MajorVersion));
        return [.. compatible, .. incompatible];

        bool IsCompatible(JavaEntry candidate)
        {
            return requiredVersion is 0 or -1 || (requiresExactVersion
                ? candidate.MajorVersion == requiredVersion
                : candidate.MajorVersion >= requiredVersion);
        }
    }

    private static void ReportJavaInstallProgress(TaskExecutionContext context, JavaInstallProgress progress)
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

    private static JavaEntry ToJavaEntry(JavaRuntimeEntry java)
    {
        return new JavaEntry
        {
            JavaPath = java.JavaPath, JavaType = java.JavaType, JavaVersion = java.JavaVersion,
            MajorVersion = java.MajorVersion, Is64bit = java.Is64Bit
        };
    }

    private static LaunchConfig CreateLaunchConfig(MinecraftInstance instance, Account account, JavaEntry java,
        MinecraftLaunchOptions options, RecentPlayTarget? target, Dictionary<string, string> placeholders)
    {
        var config = new LaunchConfig
        {
            JvmArguments = LaunchCustomization.SplitArguments(
                LaunchCustomization.Apply(options.JvmArguments, placeholders)),
            WrapperCommand = LaunchCustomization.Apply(options.WrapperCommand, placeholders),
            Account = account,
            JavaPath = java,
            LauncherName = "Portal",
            IsEnableIndependency = instance.RequiresIndependentInstance ||
                                   instance.JavaConfig?.EnableIndependentInstance == true,
            Width = options.WindowWidth,
            Height = options.WindowHeight,
            MinMemorySize = 512,
            MaxMemorySize = instance.JavaConfig?.EnableOverrideMaxMemory == true
                ? instance.JavaConfig.MinecraftMaxMemory
                : options.MaxMemory,
            SaveName = target is { Type: RecentPlayTargetType.World } ? target.Id : null,
            ServerInfo = target is
                { Type: RecentPlayTargetType.Server, ServerPort: { } port, ServerAddress: { } address }
                ? new ServerInfo { Address = address, Port = port }
                : null
        };

        ApplyGraphicsLaunchConfiguration(instance, config, placeholders);

        if (options.IsFullscreen)
        {
            config.Width = 0;
            config.Height = 0;
            config.GameArguments = config.GameArguments.Append("--fullscreen");
        }

        return config;
    }

    private static void ApplyGraphicsLaunchConfiguration(MinecraftInstance instance, LaunchConfig config,
        Dictionary<string, string> placeholders)
    {
        var entry = instance.MinecraftEntry;
        if (entry == null)
            return;

        var versionId = entry is ModifiedMinecraftEntry { HasInheritance: true } modified
            ? modified.InheritedMinecraft.Version.VersionId
            : entry.Version.VersionId;

        var graphics = instance.JavaConfig?.GraphicsBackend ?? GraphicsApi.Default;
        var version = GameVersion.Parse(versionId ?? entry.Id);
        var effective = GraphicsEnvironmentResolver.Resolve(graphics,
            instance.JavaConfig?.OpenGlRenderer, instance.JavaConfig?.VulkanRenderer, version);

        var nativesFolder = string.IsNullOrEmpty(config.NativesFolder)
            ? entry.NativesDirectoryPath ?? Path.Combine(entry.MinecraftFolderPath, "versions", entry.Id, "natives")
            : config.NativesFolder;

        var launch = GraphicsLaunchArgumentsBuilder.Build(effective, graphics, version, nativesFolder,
            Renderers.CurrentPlatform);

        if (launch.EnvironmentVariables.Count > 0)
            config.EnvironmentVariables = new Dictionary<string, string>(launch.EnvironmentVariables);
        if (launch.GameArguments.Any())
            config.GameArguments = launch.GameArguments;

        if (launch.NeedsMesaAgent)
            config.JvmArguments = config.JvmArguments.Concat(launch.JvmArguments);
    }

    private static void WriteStartupLog(MinecraftLogSession logSession, MinecraftInstance instance, LaunchConfig config)
    {
        var jvmArguments = string.Join(" ", config.JvmArguments);
        var gameArguments = string.Join(" ", config.GameArguments);
        var none = CommonLanguageManager.Instance.common_none.CurrentValue();
        List<string> lines =
        [
            LogLanguageManager.Instance.launch_startupHeader.CurrentValue(),
            string.Format(LogLanguageManager.Instance.launch_startupPortalVersion.CurrentValue(), MinecraftCoreInitializer.AppVersion),
            string.Format(LogLanguageManager.Instance.launch_startupTime.CurrentValue(), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}"),
            string.Format(LogLanguageManager.Instance.launch_startupOs.CurrentValue(), RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture),
            string.Format(LogLanguageManager.Instance.launch_startupDotnet.CurrentValue(), RuntimeInformation.FrameworkDescription),
            string.Format(LogLanguageManager.Instance.launch_startupArchitecture.CurrentValue(), RuntimeInformation.ProcessArchitecture),
            string.Empty,
            LogLanguageManager.Instance.launch_instanceInfoHeader.CurrentValue(),
            string.Format(LogLanguageManager.Instance.launch_instanceName.CurrentValue(), instance.InstanceName),
            string.Format(LogLanguageManager.Instance.launch_gameVersion.CurrentValue(), instance.VersionId),
            string.Format(LogLanguageManager.Instance.launch_versionType.CurrentValue(), instance.VersionType),
            string.Format(LogLanguageManager.Instance.launch_loaderDescription.CurrentValue(), instance.LoaderDescription),
            string.Format(LogLanguageManager.Instance.launch_gameDirectory.CurrentValue(), instance.MinecraftPath),
            string.Format(LogLanguageManager.Instance.launch_independentVersion.CurrentValue(),
                config.IsEnableIndependency ? CommonLanguageManager.Instance.common_yes.CurrentValue() : CommonLanguageManager.Instance.common_no.CurrentValue()),
            string.Empty,
            LogLanguageManager.Instance.launch_accountInfoHeader.CurrentValue(),
            string.Format(LogLanguageManager.Instance.launch_accountType.CurrentValue(), config.Account.Type),
            string.Format(LogLanguageManager.Instance.launch_playerName.CurrentValue(), config.Account.Name),
            string.Empty,
            LogLanguageManager.Instance.launch_javaInfoHeader.CurrentValue(),
            string.Format(LogLanguageManager.Instance.launch_javaVersion.CurrentValue(), config.JavaPath.JavaVersion),
            string.Format(LogLanguageManager.Instance.launch_javaType.CurrentValue(), config.JavaPath.JavaType),
            string.Format(LogLanguageManager.Instance.launch_javaMajorVersion.CurrentValue(), config.JavaPath.MajorVersion),
            string.Format(LogLanguageManager.Instance.launch_javaArchitecture.CurrentValue(),
                config.JavaPath.Is64bit ? CommonLanguageManager.Instance.common_64bit.CurrentValue() : CommonLanguageManager.Instance.common_32bit.CurrentValue()),
            string.Format(LogLanguageManager.Instance.launch_javaExecutable.CurrentValue(), config.JavaPath.JavaPath),
            string.Empty,
            LogLanguageManager.Instance.launch_memoryWindowHeader.CurrentValue(),
            string.Format(LogLanguageManager.Instance.launch_minMemory.CurrentValue(), config.MinMemorySize),
            string.Format(LogLanguageManager.Instance.launch_maxMemory.CurrentValue(), config.MaxMemorySize),
            string.Format(LogLanguageManager.Instance.launch_windowSize.CurrentValue(),
                config.IsFullscreen ? CommonLanguageManager.Instance.launch_fullscreen.CurrentValue() : $"{config.Width} × {config.Height}"),
            string.Format(LogLanguageManager.Instance.launch_fullscreenMode.CurrentValue(),
                config.IsFullscreen ? CommonLanguageManager.Instance.common_yes.CurrentValue() : CommonLanguageManager.Instance.common_no.CurrentValue()),
            string.Empty,
            LogLanguageManager.Instance.launch_jvmArgumentsHeader.CurrentValue(),
            string.IsNullOrEmpty(jvmArguments) ? none : jvmArguments,
            LogLanguageManager.Instance.launch_gameArgumentsHeader.CurrentValue(),
            string.IsNullOrEmpty(gameArguments) ? none : gameArguments,
            LogLanguageManager.Instance.launch_environmentVariablesHeader.CurrentValue(),
            config.EnvironmentVariables.Count > 0
                ? string.Join(" ", config.EnvironmentVariables.Select(pair => $"{pair.Key}={pair.Value}"))
                : none,
            string.IsNullOrEmpty(config.WrapperCommand)
                ? string.Empty
                : LogLanguageManager.Instance.launch_wrapperCommandHeader.CurrentValue(),
            string.IsNullOrEmpty(config.WrapperCommand) ? string.Empty : config.WrapperCommand,
            LogLanguageManager.Instance.launch_footer.CurrentValue()
        ];
        foreach (var line in lines.Where(line => line.Length > 0))
            logSession.Add(new MinecraftLogEntry(line, MinecraftLogLevel.Information));
    }

    private static void ObserveProcess(MinecraftInstance instance, TopLevel? topLevel, MinecraftProcess process,
        ManagedTask task, TaskExecutionContext context, MinecraftLogSession logSession, MinecraftLaunchOptions options)
    {
        instance.Config.LastPlayTime = DateTime.Now;
        context.SetRunning(CommonLanguageManager.Instance.launch_watchingProcess.CurrentValue());
        instance.IncrementPlaySessions();
        instance.StartPlayTimer();
        process.Process.OutputDataReceived += (_, data) =>
        {
            if (string.IsNullOrEmpty(data.Data))
                return;

            var entry = new MinecraftLogEntry(data.Data, GetLogLevel(data.Data));
            logSession.Add(entry);
            new RecentPlayService().RecordServerConnection(instance, data.Data);
        };
        task.AddAction(new TaskActionDefinition
        {
            Name = CommonLanguageManager.Instance.launch_copyLaunchArguments.CurrentValue(),
            Description = CommonLanguageManager.Instance.launch_copyLaunchArgumentsDescription.CurrentValue(),
            ExecuteAsync = async (_, _) =>
            {
                if (topLevel?.Clipboard == null)
                    throw new InvalidOperationException(CommonLanguageManager.Instance.launch_clipboardUnsupported.CurrentValue());
                await topLevel.Clipboard.SetTextAsync(string.Join(Environment.NewLine, process.ArgumentList));
            }
        });
        process.Exited += (_, _) =>
        {
            instance.StopPlayTimer();
            Notice(topLevel, string.Format(CommonLanguageManager.Instance.launch_processExited.CurrentValue(), instance.InstanceName), NotificationType.Success);
            if (options.GameExited != null)
                Dispatcher.UIThread.Post(options.GameExited);
            Dispatcher.UIThread.Post(() =>
            {
                if (!task.IsTerminal)
                    task.Complete();
            });
        };

        if (!IsProcessRunning(process))
        {
            instance.StopPlayTimer();
            task.Complete();
        }
    }

    private static bool IsProcessRunning(MinecraftProcess process)
    {
        try
        {
            return process.Process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task LaunchBedrockAsync(TaskExecutionContext context, MinecraftInstance instance,
        TopLevel? topLevel, MinecraftLaunchOptions options, ManagedTask task,
        MinecraftLogSession logSession, Action<Process> processStarted)
    {
        if (instance.BedrockConfig == null)
            throw new InvalidOperationException(CommonLanguageManager.Instance.launch_bedrockConfigMissing.CurrentValue());

        var placeholders = LaunchCustomization.BuildPlaceholders(instance, null, null, options);
        BedrockAuthentication? authentication = null;
        if (options.EnableBedrockAccountInjection && options.BedrockAccount is { } bedrockAccount)
        {
            context.SetRunning(CommonLanguageManager.Instance.launch_refreshingBedrockAccount.CurrentValue());
            var refreshedAccount = await new BedrockAuthenticationService().RefreshAsync(bedrockAccount,
                context.CancellationToken);
            refreshedAccount.LastLoginTime = DateTime.Now;
            options.BedrockAccountRefreshed?.Invoke(bedrockAccount, refreshedAccount);
            placeholders["{player_name}"] = refreshedAccount.Gamertag;
            placeholders["{player_uuid}"] = refreshedAccount.Xuid;
            placeholders["{account_type}"] = "bedrock";
            authentication = new BedrockAuthentication(refreshedAccount.Gamertag, refreshedAccount.Xuid,
                refreshedAccount.AccessToken, refreshedAccount.RefreshToken, refreshedAccount.ExpiresAt);
        }

        await RunBeforeLaunchCommandAsync(context, topLevel, options, placeholders);
        context.SetRunning(CommonLanguageManager.Instance.launch_startingBedrockGame.CurrentValue());

        var factory = options.BedrockLauncherFactory ?? DefaultBedrockLauncherFactory
            ?? throw new PlatformNotSupportedException(CommonLanguageManager.Instance.launch_bedrockPlatformUnsupported.CurrentValue());

        var launcher = factory(instance.BedrockConfig);
        launcher.Authentication = authentication;
        launcher.LogReceived = (message, level) =>
        {
            var text = $"[Portal/Bedrock] {message}";
            var minecraftLevel = level switch
            {
                BedrockLogLevel.Debug => MinecraftLogLevel.Debug,
                BedrockLogLevel.Warning => MinecraftLogLevel.Warning,
                BedrockLogLevel.Error => MinecraftLogLevel.Error,
                _ => MinecraftLogLevel.Information
            };
            logSession.Add(new MinecraftLogEntry(text, minecraftLevel));
            switch (level)
            {
                case BedrockLogLevel.Debug: Logger.Debug(text); break;
                case BedrockLogLevel.Warning: Logger.Warning(text); break;
                case BedrockLogLevel.Error: Logger.Error(text); break;
                default: Logger.Info(text); break;
            }
        };
        launcher.UpdateProgress = (text, progress) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (context.Task.IsTerminal)
                    return;

                context.SetRunning(text);
                context.ReportProgress(progress / 100.0);
            });
        };
        var processReported = 0;

        void ReportProcess(Process launchedProcess)
        {
            if (Interlocked.Exchange(ref processReported, 1) == 0)
                processStarted(launchedProcess);
        }

        launcher.ProcessStarted = ReportProcess;

        await launcher.Launch(context.CancellationToken);

        var process = launcher.GetProcess()
                      ?? throw new InvalidOperationException(CommonLanguageManager.Instance.launch_bedrockNoProcessInfo.CurrentValue());

        ObserveBedrockProcess(instance, topLevel, process, task, context, options);
        ReportProcess(process);
        OnGameProcessStarted(process, options, placeholders, false, instance);
        context.ReportProgress(1);
    }

    private static void ObserveBedrockProcess(MinecraftInstance instance, TopLevel? topLevel, Process process,
        ManagedTask task, TaskExecutionContext context, MinecraftLaunchOptions options)
    {
        instance.Config.LastPlayTime = DateTime.Now;
        context.SetRunning(CommonLanguageManager.Instance.launch_watchingProcess.CurrentValue());
        instance.IncrementPlaySessions();
        instance.StartPlayTimer();

        process.Exited += (_, _) =>
        {
            instance.StopPlayTimer();
            Notice(topLevel, string.Format(CommonLanguageManager.Instance.launch_processExited.CurrentValue(), instance.InstanceName), NotificationType.Success);
            if (options.GameExited != null)
                Dispatcher.UIThread.Post(options.GameExited);
            Dispatcher.UIThread.Post(() =>
            {
                if (!task.IsTerminal)
                    task.Complete();
            });
        };
        process.EnableRaisingEvents = true;

        if (!IsProcessRunning(process))
        {
            instance.StopPlayTimer();
            task.Complete();
        }
    }

    private static MinecraftLogLevel GetLogLevel(string line)
    {
        if (line.Contains("/FATAL]", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("/ERROR]", StringComparison.OrdinalIgnoreCase))
            return MinecraftLogLevel.Error;
        if (line.Contains("/WARN]", StringComparison.OrdinalIgnoreCase))
            return MinecraftLogLevel.Warning;
        if (line.Contains("/DEBUG]", StringComparison.OrdinalIgnoreCase))
            return MinecraftLogLevel.Debug;
        if (line.Contains("/TRACE]", StringComparison.OrdinalIgnoreCase))
            return MinecraftLogLevel.Trace;
        if (line.Contains("/INFO]", StringComparison.OrdinalIgnoreCase))
            return MinecraftLogLevel.Information;
        return MinecraftLogLevel.Other;
    }

    private static string GetEditionName(MinecraftInstance instance)
    {
        return instance.Type switch
        {
            MinecraftInstanceType.Java => CommonLanguageManager.Instance.launch_javaEdition.CurrentValue(),
            MinecraftInstanceType.Bedrock => CommonLanguageManager.Instance.launch_bedrockEdition.CurrentValue(),
            _ => "Minecraft"
        };
    }

    private static void Notice(TopLevel? topLevel, string message, NotificationType type)
    {
        if (topLevel == null)
            return;
        Dispatcher.UIThread.Post(() => topLevel.Notice(message, type));
    }

    private static void NoticeMissingJava(TopLevel? topLevel, MissingJavaVersionException exception)
    {
        if (topLevel == null)
            return;

        Dispatcher.UIThread.Post(() => topLevel.Notice(new NotificationOptions
        {
            Content = exception.Message,
            Type = NotificationType.Error,
            Expiration = TimeSpan.FromSeconds(10),
            OperateButtons =
            [
                new OperateButtonEntry(CommonLanguageManager.Instance.launch_downloadJava.CurrentValue(), _ =>
                {
                    OpenJavaDownloadPage?.Invoke();
                }, true)
            ]
        }));
    }

    private static string GetFailureReason(Exception exception)
    {
        return exception switch
        {
            FileNotFoundException => CommonLanguageManager.Instance.launch_failureMissingFiles.CurrentValue(),
            UnauthorizedAccessException => CommonLanguageManager.Instance.launch_failureNoPermission.CurrentValue(),
            _ => exception.Message
        };
    }
}

public enum MinecraftLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Other
}

public sealed record MinecraftLogEntry(string Text, MinecraftLogLevel Level);

public sealed class MinecraftLogSession
{
    private const int MaximumBufferedLogLines = 10_000;
    private readonly Queue<MinecraftLogEntry> _entries = new();
    private readonly object _syncRoot = new();

    public MinecraftLogSession(MinecraftInstance instance)
    {
        Instance = instance;
    }

    public MinecraftInstance Instance { get; }
    public event Action<MinecraftLogEntry>? LogReceived;

    internal void Add(MinecraftLogEntry entry)
    {
        lock (_syncRoot)
        {
            _entries.Enqueue(entry);
            if (_entries.Count > MaximumBufferedLogLines)
                _entries.Dequeue();
        }

        LogReceived?.Invoke(entry);
    }

    public IReadOnlyList<MinecraftLogEntry> GetEntries()
    {
        lock (_syncRoot)
        {
            return _entries.ToArray();
        }
    }
}

public sealed class MinecraftLaunchOptions
{
    public MinecraftAccount? Account { get; init; }
    public BedrockAccount? BedrockAccount { get; init; }
    public bool EnableBedrockAccountInjection { get; init; }
    public bool EnableGameOverlay { get; init; }
    public Action<Process, MinecraftInstance>? ShowGameOverlay { get; init; }
    public IReadOnlyList<JavaRuntimeEntry> JavaRuntimes { get; init; } = [];
    public IReadOnlyDictionary<int, string> JavaVersionDefaults { get; init; } = new Dictionary<int, string>();
    public int WindowWidth { get; init; }
    public int WindowHeight { get; init; }
    public bool IsFullscreen { get; init; }
    public int MaxMemory { get; init; }
    public bool AutoSetJavaHighPerformanceGpu { get; init; }
    public bool AutoOptimizeMemoryBeforeGameLaunch { get; init; }
    public bool SetChineseLanguageOnLaunch { get; init; }
    public string? WindowTitle { get; init; }
    public string? JvmArguments { get; init; }
    public string? BeforeLaunchCommand { get; init; }
    public string? AfterLaunchCommand { get; init; }
    public string? WrapperCommand { get; init; }
    public Action? GameStarted { get; init; }
    public Action? GameExited { get; init; }
    public Action<MinecraftAccount, MinecraftAccount>? AccountRefreshed { get; init; }
    public Action<BedrockAccount, BedrockAccount>? BedrockAccountRefreshed { get; init; }
    public Action<MinecraftLogSession>? OpenLog { get; init; }

    public Func<int, JavaInstallProgressHandler, CancellationToken, Task<JavaRuntimeEntry?>>? InstallMissingJava
    {
        get;
        init;
    }

    public Func<BedrockInstanceConfig, IBedrockLaunch>? BedrockLauncherFactory { get; init; }
    public IReadOnlyList<string> ResourceSourceRoots { get; init; } = [];
}