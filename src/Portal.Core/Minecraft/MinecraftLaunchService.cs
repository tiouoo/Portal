using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Components.Parser;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Extensions;
using MinecraftLaunch.Launch;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Minecraft.Services;
using Portal.Core.Operations.Account;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using Avalonia.Controls.Notifications;
using Tio.Avalonia.Standard.Standard.Ui;

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
        var logSession = instance.Type == MinecraftInstanceType.Java ? new MinecraftLogSession(instance) : null;
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
                    IsVisible = _ => logSession != null && options.OpenLog != null
                }
            ]
        });
        ManagedTask? verifyAccount = null;
        ManagedTask? selectJava = null;
        ManagedTask? buildArguments = null;
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
        }
        var startGame = task.CreateChild(new TaskOptions
        {
            Name = instance.Type == MinecraftInstanceType.Bedrock ? "启动基岩版" : "启动 Minecraft",
            Description = instance.Type == MinecraftInstanceType.Bedrock ? "等待启动" : "等待启动参数构建完成",
            Progress = 0
        });
        task.Start();
        _ = RunWorkflowAsync(instance, topLevel, options, target, task, verifyAccount, selectJava, buildArguments, startGame, logSession,
            launchedProcess =>
            {
                process = launchedProcess;
                launchCompleted = true;
                task.RefreshActions();
            });
        return task.Completion;
    }

    private static async Task RunWorkflowAsync(MinecraftInstance instance, TopLevel? topLevel, MinecraftLaunchOptions options,
        RecentPlayTarget? target, ManagedTask task,
        ManagedTask? verifyAccount, ManagedTask? selectJava, ManagedTask? buildArguments, ManagedTask startGame,
        MinecraftLogSession? logSession, Action<Process> processStarted)
    {
        try
        {
            if (instance.Type == MinecraftInstanceType.Bedrock)
            {
                startGame.Start(context => LaunchBedrockAsync(context, instance, topLevel, options, task, processStarted));
                await startGame.Completion;
                ThrowIfFailed(startGame);
                return;
            }

            if (instance.Type != MinecraftInstanceType.Java || instance.MinecraftEntry == null)
                throw new InvalidOperationException("当前仅支持启动 Java 版 Minecraft 实例。");

            Account? account = null;
            JavaEntry? java = null;
            LaunchConfig? config = null;

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
                java = await SelectJavaAsync(instance, options, context.CancellationToken);
                context.ReportProgress(1);
            });
            await selectJava.Completion;
            ThrowIfFailed(selectJava);

            buildArguments!.Start(context =>
            {
                context.SetRunning("正在应用实例与全局游戏设置");
                config = CreateLaunchConfig(instance, account!, java!, options, target);
                context.ReportProgress(1);
                return Task.CompletedTask;
            });
            await buildArguments.Completion;
            ThrowIfFailed(buildArguments);

            startGame.Start(context => StartGameStepAsync(context, instance, config!, target, topLevel, task, logSession!, processStarted));
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
        LaunchConfig config, RecentPlayTarget? target, TopLevel? topLevel, ManagedTask task, MinecraftLogSession logSession,
        Action<Process> processStarted)
    {
        if (instance.Layout != null)
        {
            context.SetRunning("正在检查外部实例依赖");
            var downloader = new MinecraftResourceDownloader(instance.MinecraftEntry!);
            await downloader.VerifyAndDownloadDependenciesAsync(cancellationToken: context.CancellationToken);
        }
        context.SetRunning("正在启动 Minecraft 进程");
        var parser = new MinecraftParser(instance.MinecraftEntry!.MinecraftFolderPath);
        var mcProcess = await new MinecraftRunner(config, parser)
            .RunAsync(instance.MinecraftEntry, context.CancellationToken);
        if (mcProcess == null)
            throw new InvalidOperationException("Minecraft 启动器未返回进程信息。");
        ObserveProcess(instance, topLevel, mcProcess, task, context, logSession);
        processStarted(mcProcess.Process);
        context.ReportProgress(1);
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
        CancellationToken cancellationToken)
    {
        var javaConfig = instance.JavaConfig
                         ?? throw new InvalidOperationException("Java 版实例配置缺失。");
        var preferred = javaConfig.EnableSpecificJava ? javaConfig.SpecificJavaEntry : null;
        var candidates = preferred != null ? [preferred] : options.JavaRuntimes.ToList();
        if (candidates.Count == 0 && options.EnableAutoSelectJava)
            candidates = (await JavaRuntimeManager.ScanAsync(cancellationToken)).ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("没有可用的 Java 运行时，请在设置中添加 Java。");

        var javaEntries = candidates.Select(ToJavaEntry).ToList();
        JavaEntry? selected;
        if (preferred != null)
        {
            selected = javaEntries[0];
        }
        else if (options.EnableAutoSelectJava)
        {
            selected = instance.MinecraftEntry!.GetAppropriateJava(javaEntries);
        }
        else
        {
            selected = options.DefaultJavaRuntime is { } defaultJava ? ToJavaEntry(defaultJava) : javaEntries[0];
            var requiredVersion = instance.MinecraftEntry!.GetAppropriateJavaVersion();
            if (requiredVersion > 0 && selected.MajorVersion < requiredVersion)
                selected = instance.MinecraftEntry.GetAppropriateJava(javaEntries);
        }
        return selected ?? throw new InvalidOperationException("找不到与当前 Minecraft 版本兼容的 Java 运行时。");
    }

    private static JavaEntry ToJavaEntry(JavaRuntimeEntry java) => new()
    {
        JavaPath = java.JavaPath, JavaType = java.JavaType, JavaVersion = java.JavaVersion,
        MajorVersion = java.MajorVersion, Is64bit = java.Is64Bit
    };

    private static LaunchConfig CreateLaunchConfig(MinecraftInstance instance, Account account, JavaEntry java,
        MinecraftLaunchOptions options, RecentPlayTarget? target) => new()
    {
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

    private static void ObserveProcess(MinecraftInstance instance, TopLevel? topLevel, MinecraftProcess process,
        ManagedTask task, TaskExecutionContext context, MinecraftLogSession logSession)
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
        Action<Process> processStarted)
    {
        context.SetRunning("正在启动基岩版游戏");

        if (instance.BedrockConfig == null)
            throw new InvalidOperationException("基岩版实例配置缺失。");

        var factory = options.BedrockLauncherFactory ?? DefaultBedrockLauncherFactory
                       ?? throw new PlatformNotSupportedException("当前平台不支持启动基岩版。");

        var launcher = factory(instance.BedrockConfig);
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

        await launcher.Launch();

        var process = launcher.GetProcess()
                      ?? throw new InvalidOperationException("基岩版启动器未返回进程信息。");

        ObserveBedrockProcess(instance, topLevel, process, task, context);
        processStarted(process);
        context.ReportProgress(1);
    }

    private static void ObserveBedrockProcess(MinecraftInstance instance, TopLevel? topLevel, Process process,
        ManagedTask task, TaskExecutionContext context)
    {
        instance.Config.LastPlayTime = DateTime.Now;
        context.SetRunning("启动完成，正在监视 Minecraft 进程");
        instance.IncrementPlaySessions();
        instance.StartPlayTimer();

        process.Exited += (_, _) =>
        {
            instance.StopPlayTimer();
            Notice(topLevel, $"{instance.InstanceName} 已退出", NotificationType.Success);
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
    public IReadOnlyList<JavaRuntimeEntry> JavaRuntimes { get; init; } = [];
    public JavaRuntimeEntry? DefaultJavaRuntime { get; init; }
    public bool EnableAutoSelectJava { get; init; }
    public int WindowWidth { get; init; }
    public int WindowHeight { get; init; }
    public int MaxMemory { get; init; }
    public Action<MinecraftAccount, MinecraftAccount>? AccountRefreshed { get; init; }
    public Action<MinecraftLogSession>? OpenLog { get; init; }
    public Func<BedrockInstanceConfig, IBedrockLaunch>? BedrockLauncherFactory { get; init; }
}
