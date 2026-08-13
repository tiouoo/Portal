using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Launch;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Graphics;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Minecraft.Services;
using Portal.Core.SystemResources;
using Portal.Core.Operations.Account;
using Portal.Core.Operations.Java;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using Avalonia.Controls.Notifications;
using Tio.Avalonia.Standard.Standard.Ui;
using Tio.Avalonia.Standard.Modules.DiskIO;
using ModLoaderType = MinecraftLaunch.Base.Enums.ModLoaderType;

namespace Portal.Core.Minecraft;

public static class MinecraftLaunchService
{
    public static Func<BedrockInstanceConfig, IBedrockLaunch>? DefaultBedrockLauncherFactory { get; set; }
    public static Task LaunchAsync(MinecraftInstance instance, TopLevel? topLevel, MinecraftLaunchOptions options,
        RecentPlayTarget? target = null)
    {
        topLevel?.Notice($"启动 {instance.InstanceName}");
        var launchCompleted = false;
        Process? process = null;
        var logSession = new MinecraftLogSession(instance);
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = $"启动 {GetEditionName(instance)} {instance.InstanceName}",
            Description = "正在准备启动流程",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消启动流程",
                    Description = "取消启动流程及其子任务。",
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
                    Name = "结束进程",
                    Description = "结束 Minecraft 及其子进程。",
                    ExecuteAsync = (_, _) =>
                    {
                        if (process == null)
                            throw new InvalidOperationException("Minecraft 进程尚未创建或已无法访问。");
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                        return Task.CompletedTask;
                    },
                    IsVisible = managedTask => launchCompleted && !managedTask.IsTerminal &&
                                               process != null && IsProcessRunning(process)
                },
                new TaskActionDefinition
                {
                    Name = "查看日志",
                    Description = "打开本次启动任务的 Minecraft 实时日志。",
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
                Name = "验证游戏账户", Description = "等待验证", Progress = 0
            });
            selectJava = task.CreateChild(new TaskOptions
            {
                Name = "选择 Java 运行时", Description = "等待账户验证完成", Progress = 0
            });
            buildArguments = task.CreateChild(new TaskOptions
            {
                Name = "构建启动参数", Description = "等待 Java 运行时选择完成", Progress = 0
            });
            completeResources = task.CreateChild(new TaskOptions
            {
                Name = "补全游戏资源", Description = "等待启动参数构建完成", Progress = 0
            });
        }
        var startGame = task.CreateChild(new TaskOptions
        {
            Name = instance.Type == MinecraftInstanceType.Bedrock ? "启动基岩版" : "启动 Minecraft",
            Description = instance.Type == MinecraftInstanceType.Bedrock ? "等待启动" : "等待启动参数构建完成",
            Progress = 0
        });
        task.Start();
        RunWorkflowAsync(instance, topLevel, options, target, task, verifyAccount, selectJava, buildArguments, completeResources,
            startGame, logSession,
            launchedProcess =>
            {
                process = launchedProcess;
                launchCompleted = true;
                task.RefreshActions();
            }).ContinueWith(completedTask => Logger.Error($"启动 Minecraft 实例工作流异常结束：{instance.InstanceName}", completedTask.Exception!),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        return task.Completion;
    }

    private static async Task RunWorkflowAsync(MinecraftInstance instance, TopLevel? topLevel, MinecraftLaunchOptions options,
        RecentPlayTarget? target, ManagedTask task,
        ManagedTask? verifyAccount, ManagedTask? selectJava, ManagedTask? buildArguments, ManagedTask? completeResources,
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
                throw new InvalidOperationException("当前仅支持启动 Java 版 Minecraft 实例。");

            Account? account = null;
            JavaEntry? java = null;
            LaunchConfig? config = null;
            Dictionary<string, string>? placeholders = null;
            IReadOnlyDictionary<string, string>? highPerformanceGpuEnvironment = null;

            verifyAccount!.Start(async context =>
            {
                context.SetRunning("正在验证游戏账户");
                account = await VerifyAccountAsync(options);
                context.ReportProgress(1);
            });
            await verifyAccount.Completion;
            ThrowIfFailed(verifyAccount);

            selectJava!.Start(async context =>
            {
                context.SetRunning("正在检查可用 Java 运行时");
                java = await SelectJavaAsync(instance, options, context, context.CancellationToken);
                if (options.AutoSetJavaHighPerformanceGpu)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        HighPerformanceGpuService.TrySetWindowsHighPerformanceGpuPreference(java.JavaPath);
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        context.SetDescription("正在解析高性能显卡环境");
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
                context.SetRunning("正在应用实例与全局游戏设置");
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
            Notice(topLevel, "取消任务", NotificationType.Information);
        }
        catch (Exception exception)
        {
            if (!task.IsTerminal)
                task.Fault(exception);
            Notice(topLevel, $"启动失败：{GetFailureReason(exception)}", NotificationType.Error);
        }
    }

    private static void ThrowIfFailed(ManagedTask task)
    {
        if (task.Exception != null)
            throw new InvalidOperationException(task.Exception.Message, task.Exception);
        task.CancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task StartGameStepAsync(TaskExecutionContext context, MinecraftInstance instance,
        LaunchConfig config, TopLevel? topLevel, ManagedTask task, MinecraftLogSession logSession,
        MinecraftLaunchOptions options, Dictionary<string, string> placeholders, Action<Process> processStarted)
    {
        await RunBeforeLaunchCommandAsync(context, topLevel, options, placeholders);
        if (options.AutoOptimizeMemoryBeforeGameLaunch && OperatingSystem.IsWindows())
        {
            context.SetRunning("正在优化系统内存");
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
                Logger.Warning($"游戏启动前内存优化失败，将继续启动游戏。{Environment.NewLine}{exception}");
                Notice(topLevel, $"内存优化未完成，将继续启动游戏：{exception.Message}", NotificationType.Warning);
            }
        }
        context.SetRunning("正在启动 Minecraft 进程");
        await PrepareGraphicsBeforeLaunchAsync(instance, config, context.CancellationToken);
        if (options.SetChineseLanguageOnLaunch && instance.MinecraftEntry != null)
        {
            context.SetRunning("正在设置游戏语言");
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
            throw new InvalidOperationException("Minecraft 启动器未返回进程信息。");
        ObserveProcess(instance, topLevel, mcProcess, task, context, logSession, options);
        processStarted(mcProcess.Process);
        OnGameProcessStarted(mcProcess.Process, options, placeholders, overrideWindowTitle: true, instance);
        context.ReportProgress(1);
    }

    private static async Task PrepareGraphicsBeforeLaunchAsync(MinecraftInstance instance, LaunchConfig config,
        CancellationToken cancellationToken)
    {
        var entry = instance.MinecraftEntry;
        if (entry == null || instance.JavaConfig == null)
            return;

        string? versionId = entry is ModifiedMinecraftEntry { HasInheritance: true } modified
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

        string nativeDir = Path.Combine(entry.NativesDirectoryPath ??
            Path.Combine(entry.MinecraftFolderPath, "versions", entry.Id, "natives"), "mesa-loader");
        Directory.CreateDirectory(nativeDir);

        string jarPath = await MesaLoaderService.EnsureMesaLoaderAsync(cancellationToken);

        string agent = GraphicsLaunchArgumentsBuilder.BuildJavaAgent(jarPath, mesaDriverName);
        config.JvmArguments = config.JvmArguments.Append("-javaagent:" + agent);
    }

    private static async Task RunBeforeLaunchCommandAsync(TaskExecutionContext context, TopLevel? topLevel,
        MinecraftLaunchOptions options, Dictionary<string, string> placeholders)
    {
        var command = LaunchCustomization.Apply(options.BeforeLaunchCommand, placeholders);
        if (string.IsNullOrWhiteSpace(command))
            return;

        context.SetRunning("正在执行启动前命令");
        var exitCode = await LaunchCustomization.RunShellCommandAsync(command,
            placeholders.GetValueOrDefault("{game_dir}"), context.CancellationToken);
        if (exitCode != 0)
            Notice(topLevel, $"启动前命令以退出代码 {exitCode} 结束", NotificationType.Warning);
    }

    private static void OnGameProcessStarted(Process process, MinecraftLaunchOptions options,
        Dictionary<string, string> placeholders, bool overrideWindowTitle, MinecraftInstance instance)
    {
        placeholders["{process_id}"] = process.Id.ToString();

        if (OperatingSystem.IsWindows() && options.EnableGameOverlay && options.ShowGameOverlay != null)
        {
            Dispatcher.UIThread.Post(() => options.ShowGameOverlay(process, instance));
        }

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
        context.SetRunning("正在检查游戏资源");
        var downloader = new MinecraftResourceDownloader(entry)
        {
            SourceRootDirectories = options.ResourceSourceRoots
        };

        // 阶段 1：检查资源文件
        await RunStepAsync(context, "检查资源文件", "正在验证本地资源文件完整性", async step =>
        {
            await Task.Factory.StartNew(
                    () => downloader.VerifyDependenciesAsync(fileVerificationParallelism: 2,
                        cancellationToken: step.CancellationToken),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default)
                .Unwrap();

            var copyCount = downloader.CopyItems.Count;
            var downloadCount = downloader.DependenciesToDownload.Count;
            step.SetDescription(copyCount + downloadCount == 0
                ? "所有资源文件均已就绪，无需下载或复制"
                : $"发现 {copyCount} 个文件可复制、{downloadCount} 个文件需下载");
            step.ReportProgress(1);
        });

        // 阶段 2：复制本地资源文件
        if (downloader.CopyItems.Count > 0)
        {
            await RunStepAsync(context, "复制本地资源文件", "正在复制本地资源文件", step =>
            {
                AttachCopyProgressReporter(step, downloader);
                return Task.Run(() => downloader.CopyDependencies(fileVerificationParallelism: 4,
                    step.CancellationToken), step.CancellationToken);
            });
        }

        // 阶段 3：下载剩余资源文件
        if (downloader.DependenciesToDownload.Count > 0)
        {
            await RunStepAsync(context, "下载资源文件", "正在下载资源文件", async step =>
            {
                AttachDownloadProgressReporter(step, downloader);
                var result = await Task.Factory.StartNew(
                        () => downloader.DownloadDependenciesAsync(step.CancellationToken),
                        CancellationToken.None,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default)
                    .Unwrap();
                if (result.Failed.Any())
                    throw new IOException($"资源补全失败：{result.Failed.Count()} 个文件下载失败。");
                step.ReportProgress(1);
            });
        }

        context.ReportProgress(1);
        context.SetDescription("资源补全完成");
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

    private static void AttachDownloadProgressReporter(TaskExecutionContext context, MinecraftResourceDownloader downloader)
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
        var files = progress.TotalCount > 0 ? $"{progress.CompletedCount}/{progress.TotalCount} 个文件" : "正在准备复制";
        var transferred = progress.TotalBytes > 0
            ? $"，{DefaultDownloader.FormatSize(progress.CopiedBytes)} / {DefaultDownloader.FormatSize(progress.TotalBytes)}"
            : string.Empty;
        var currentFile = !string.IsNullOrWhiteSpace(progress.CurrentFile)
            ? $"，{Path.GetFileName(progress.CurrentFile)}"
            : string.Empty;
        return $"正在复制本地资源：{files}{transferred}{currentFile}";
    }

    private static async Task RunStepAsync(TaskExecutionContext context, string name, string description,
        Func<TaskExecutionContext, Task> operation)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var step = context.CreateChild(new TaskOptions { Name = name, Description = description, Progress = 0 }, operation);
        step.Start();
        await step.Completion;
        if (step.Exception is not null) throw new InvalidOperationException(step.Exception.Message, step.Exception);
        context.CancellationToken.ThrowIfCancellationRequested();
    }

    private static string FormatResourceProgress(int completedCount, int totalCount, long downloadedBytes, long totalBytes,
        double speed, TimeSpan estimatedRemaining)
    {
        var files = totalCount > 0 ? $"{completedCount}/{totalCount} 个文件" : "正在准备下载";
        var transferred = totalBytes > 0
            ? $"，{DefaultDownloader.FormatSize(downloadedBytes)} / {DefaultDownloader.FormatSize(totalBytes)}"
            : string.Empty;
        var speedText = speed > 0 ? $"，{DefaultDownloader.FormatSize(speed, true)}" : string.Empty;
        // var remaining = estimatedRemaining > TimeSpan.Zero ? $"，剩余约 {estimatedRemaining:mm\\:ss}" : string.Empty;
        return $"正在补全资源：{files}{transferred}{speedText}";
    }

    private static async Task<Account> VerifyAccountAsync(MinecraftLaunchOptions options)
    {
        var account = options.Account
                      ?? throw new InvalidOperationException("请先在账户设置中选择用于启动游戏的账户。");
        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException("所选账户没有有效的玩家名。");

        switch (account.AccountType)
        {
            case AccountType.Offline:
                return new OfflineAccount(account.Name, account.Uuid ?? MinecraftAccount.GetMinecraftOfflineUuid(account.Name),
                    account.AccessToken ?? Guid.NewGuid().ToString("N"));
            case AccountType.Yggdrasil:
                if (!account.Uuid.HasValue || string.IsNullOrWhiteSpace(account.AccessToken) ||
                    string.IsNullOrWhiteSpace(account.ClientToken) || string.IsNullOrWhiteSpace(account.YggdrasilServerUrl))
                    throw new InvalidOperationException("外置登录账户信息不完整，请重新登录。");
                return new YggdrasilAccount(account.Name, account.Uuid.Value, account.AccessToken, account.ClientToken,
                    account.YggdrasilServerUrl) { MetaData = account.MetaData };
            case AccountType.Microsoft:
                var refreshed = await AccountRefresher.RefreshMicrosoft(account)
                                ?? throw new InvalidOperationException("微软账户令牌刷新失败，请重新登录。");
                options.AccountRefreshed?.Invoke(account, refreshed);
                if (!refreshed.Uuid.HasValue || string.IsNullOrWhiteSpace(refreshed.AccessToken) ||
                    string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                    throw new InvalidOperationException("微软账户刷新后缺少必要的验证信息。");
                return new MicrosoftAccount(refreshed.Name, refreshed.Uuid.Value, refreshed.AccessToken,
                    refreshed.RefreshToken, refreshed.LastRefreshTime ?? DateTime.Now);
            default:
                throw new InvalidOperationException("不支持的账户类型。");
        }
    }

    private static async Task<JavaEntry> SelectJavaAsync(MinecraftInstance instance, MinecraftLaunchOptions options,
        TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var javaConfig = instance.JavaConfig
                         ?? throw new InvalidOperationException("Java 版实例配置缺失。");
        var preferred = javaConfig.EnableSpecificJava ? javaConfig.SpecificJavaEntry : null;
        var candidates = preferred != null ? [preferred] : options.JavaRuntimes.ToList();
        var requiredVersion = instance.MinecraftEntry!.GetAppropriateJavaVersion();

        var selected = await SelectViableJavaAsync(instance, preferred, candidates, cancellationToken);
        if (selected is not null) return selected;

        if (options.InstallMissingJava is not null && requiredVersion > 0)
        {
            context.SetRunning($"正在安装 Java {requiredVersion}");
            var installed = await options.InstallMissingJava(requiredVersion,
                progress => ReportJavaInstallProgress(context, progress), cancellationToken);
            if (installed is not null)
            {
                var usable = await JavaRuntimeVerifier.IsUsableAsync(installed.JavaPath, installed.MajorVersion, cancellationToken);
                if (usable) return ToJavaEntry(installed);
                throw new InvalidOperationException(
                    $"自动安装的 Java {installed.JavaVersion} 模块不完整（缺少 jdk.zipfs / jdk.unsupported），无法启动 Minecraft。请更换完整的 Java 运行时。");
            }
        }

        // 用户拒绝自动安装后，回退到磁盘扫描（保留原有行为）。
        if (candidates.Count == 0)
            candidates = (await JavaRuntimeManager.ScanAsync(cancellationToken)).ToList();
        selected = await SelectViableJavaAsync(instance, preferred, candidates, cancellationToken);
        if (selected is not null) return selected;

        throw new InvalidOperationException(
            "没有可用的 Java 运行时，或已添加的 Java 运行时均模块不完整（缺少 jdk.zipfs / jdk.unsupported），无法启动 Minecraft。请在设置中重新添加完整的 Java 运行时。");
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
            modified.ModLoaders.Any(loader => loader.Type is ModLoaderType.Forge or ModLoaderType.NeoForge);

        var compatible = javaEntries.Where(IsCompatible).ToList();
        var incompatible = javaEntries.Where(candidate => !IsCompatible(candidate)).ToList();
        compatible.Sort((a, b) => a.MajorVersion.CompareTo(b.MajorVersion));
        return [.. compatible, .. incompatible];

        bool IsCompatible(JavaEntry candidate) => requiredVersion is 0 or -1 || (requiresExactVersion
            ? candidate.MajorVersion == requiredVersion
            : candidate.MajorVersion >= requiredVersion);
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
                    ? $"{progress.Stage}，下载速度：{DefaultDownloader.FormatSize(progress.SpeedBytesPerSecond, true)}"
                    : progress.Stage);
            }
            catch (InvalidOperationException)
            {
                // 任务已进入终态，忽略进度更新
            }
        });
    }

    private static JavaEntry ToJavaEntry(JavaRuntimeEntry java) => new()
    {
        JavaPath = java.JavaPath, JavaType = java.JavaType, JavaVersion = java.JavaVersion,
        MajorVersion = java.MajorVersion, Is64bit = java.Is64Bit
    };

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
            ServerInfo = target is { Type: RecentPlayTargetType.Server, ServerPort: { } port, ServerAddress: { } address }
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

        string? versionId = entry is ModifiedMinecraftEntry { HasInheritance: true } modified
            ? modified.InheritedMinecraft.Version.VersionId
            : entry.Version.VersionId;

        var graphics = instance.JavaConfig?.GraphicsBackend ?? GraphicsApi.Default;
        var version = GameVersion.Parse(versionId ?? entry.Id);
        var effective = GraphicsEnvironmentResolver.Resolve(graphics,
            instance.JavaConfig?.OpenGlRenderer, instance.JavaConfig?.VulkanRenderer, version);

        string? nativesFolder = string.IsNullOrEmpty(config.NativesFolder)
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
        List<string> lines =
        [
            "==================== Portal 启动前置信息 ====================",
            $"Portal 版本：{MinecraftCoreInitializer.AppVersion}",
            $"启动时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"操作系统：{RuntimeInformation.OSDescription}（{RuntimeInformation.OSArchitecture}）",
            $".NET 运行时：{RuntimeInformation.FrameworkDescription}",
            $"进程架构：{RuntimeInformation.ProcessArchitecture}",
            string.Empty,
            "---------------------- 实例信息 ----------------------",
            $"实例名称：{instance.InstanceName}",
            $"游戏版本：{instance.VersionId}",
            $"版本类型：{instance.VersionType}",
            $"加载器：{instance.LoaderDescription}",
            $"游戏目录：{instance.MinecraftPath}",
            $"独立版本：{(config.IsEnableIndependency ? "是" : "否")}",
            string.Empty,
            "---------------------- 账户信息 ----------------------",
            $"账户类型：{config.Account.Type}",
            $"玩家名：{config.Account.Name}",
            string.Empty,
            "---------------------- Java 运行时 ----------------------",
            $"Java 版本：{config.JavaPath.JavaVersion}",
            $"Java 类型：{config.JavaPath.JavaType}",
            $"Java 主版本：{config.JavaPath.MajorVersion}",
            $"Java 架构：{(config.JavaPath.Is64bit ? "64 位" : "32 位")}",
            $"Java 可执行文件：{config.JavaPath.JavaPath}",
            string.Empty,
            "---------------------- 内存与窗口 ----------------------",
            $"最小内存：{config.MinMemorySize} MB",
            $"最大内存：{config.MaxMemorySize} MB",
            $"窗口大小：{(config.IsFullscreen ? "全屏" : $"{config.Width} × {config.Height}")}",
            $"全屏模式：{(config.IsFullscreen ? "是" : "否")}",
            string.Empty,
            "---------------------- JVM 参数 ----------------------",
            string.IsNullOrEmpty(jvmArguments) ? "（无）" : jvmArguments,
            "---------------------- 游戏参数 ----------------------",
            string.IsNullOrEmpty(gameArguments) ? "（无）" : gameArguments,
            "---------------------- 环境变量 ----------------------",
            config.EnvironmentVariables.Count > 0
                ? string.Join(" ", config.EnvironmentVariables.Select(pair => $"{pair.Key}={pair.Value}"))
                : "（无）",
            string.IsNullOrEmpty(config.WrapperCommand)
                ? string.Empty
                : "---------------------- 包装命令 ----------------------",
            string.IsNullOrEmpty(config.WrapperCommand) ? string.Empty : config.WrapperCommand,
            "==========================================================="
        ];
        foreach (var line in lines.Where(line => line.Length > 0))
            logSession.Add(new MinecraftLogEntry(line, MinecraftLogLevel.Information));
    }

    private static void ObserveProcess(MinecraftInstance instance, TopLevel? topLevel, MinecraftProcess process,
        ManagedTask task, TaskExecutionContext context, MinecraftLogSession logSession, MinecraftLaunchOptions options)
    {
        instance.Config.LastPlayTime = DateTime.Now;
        context.SetRunning("启动完成，正在监视 Minecraft 进程");
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
            Name = "复制启动参数",
            Description = "复制本次启动使用的完整 Java 参数。",
            ExecuteAsync = async (_, _) =>
            {
                if (topLevel?.Clipboard == null)
                    throw new InvalidOperationException("当前窗口不支持访问系统剪贴板。");
                await topLevel.Clipboard.SetTextAsync(string.Join(Environment.NewLine, process.ArgumentList));
            }
        });
        process.Exited += (_, _) =>
        {
            instance.StopPlayTimer();
            Notice(topLevel, $"{instance.InstanceName} 已退出", NotificationType.Success);
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
            throw new InvalidOperationException("基岩版实例配置缺失。");

        var placeholders = LaunchCustomization.BuildPlaceholders(instance, null, null, options);
        BedrockAuthentication? authentication = null;
        if (options.EnableBedrockAccountInjection && options.BedrockAccount is { } bedrockAccount)
        {
            context.SetRunning("正在刷新基岩版 Xbox 账户");
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
        context.SetRunning("正在启动基岩版游戏");

        var factory = options.BedrockLauncherFactory ?? DefaultBedrockLauncherFactory
                       ?? throw new PlatformNotSupportedException("当前平台不支持启动基岩版。");

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
                      ?? throw new InvalidOperationException("基岩版启动器未返回进程信息。");

        ObserveBedrockProcess(instance, topLevel, process, task, context, options);
        ReportProcess(process);
        OnGameProcessStarted(process, options, placeholders, overrideWindowTitle: false, instance);
        context.ReportProgress(1);
    }

    private static void ObserveBedrockProcess(MinecraftInstance instance, TopLevel? topLevel, Process process,
        ManagedTask task, TaskExecutionContext context, MinecraftLaunchOptions options)
    {
        instance.Config.LastPlayTime = DateTime.Now;
        context.SetRunning("启动完成，正在监视 Minecraft 进程");
        instance.IncrementPlaySessions();
        instance.StartPlayTimer();

        process.Exited += (_, _) =>
        {
            instance.StopPlayTimer();
            Notice(topLevel, $"{instance.InstanceName} 已退出", NotificationType.Success);
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

    private static string GetEditionName(MinecraftInstance instance) => instance.Type switch
    {
        MinecraftInstanceType.Java => "Java 版",
        MinecraftInstanceType.Bedrock => "基岩版",
        _ => "Minecraft"
    };

    private static void Notice(TopLevel? topLevel, string message, NotificationType type)
    {
        if (topLevel == null)
            return;
        Dispatcher.UIThread.Post(() => NotificationGateway.Notice(topLevel, message, type));
    }

    private static string GetFailureReason(Exception exception) => exception switch
    {
        FileNotFoundException => "缺少游戏或 Java 文件。",
        UnauthorizedAccessException => "没有访问游戏目录或 Java 文件的权限。",
        _ => exception.Message
    };
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
    private readonly object _syncRoot = new();
    private readonly Queue<MinecraftLogEntry> _entries = new();

    public MinecraftInstance Instance { get; }
    public event Action<MinecraftLogEntry>? LogReceived;

    public MinecraftLogSession(MinecraftInstance instance)
    {
        Instance = instance;
    }

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
            return _entries.ToArray();
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
    public JavaRuntimeEntry? DefaultJavaRuntime { get; init; }
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
    public Func<int, JavaInstallProgressHandler, CancellationToken, Task<JavaRuntimeEntry?>>? InstallMissingJava { get; init; }
    public Func<BedrockInstanceConfig, IBedrockLaunch>? BedrockLauncherFactory { get; init; }
    public IReadOnlyList<string> ResourceSourceRoots { get; init; } = [];
}
