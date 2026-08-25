using System.Text.Json;
using Iridium.Authentication.Models;
using Iridium.Java;
using Iridium.Launch.Models;
using Iridium.Minecraft.Models;
using Iridium.Launch;
using Iridium.Minecraft;
using MinecraftLaunch.Utilities;
using Portal.Core.Json;
using Portal.Core.Minecraft;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Graphics;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Java;
using Portal.Core.Minecraft.Services;
using Portal.Core.Module.Ipc;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using LoaderType = Iridium.Enums.LoaderType;

namespace Portal.Desktop;

internal static class CliHeadlessLauncher
{
    private sealed class CliSettings
    {
        public List<JavaRuntimeEntry> JavaRuntimes { get; set; } = [];
        public Dictionary<int, string> JavaVersionDefaultPaths { get; set; } = new();
        public MinecraftAccount? UsingMinecraftMinecraftAccount { get; set; }
        public int MinecraftMaxMemory { get; set; } = 4096;
        public bool EnableFullscreen { get; set; }
        public bool EnableGameOverlay { get; set; } = true;
        public int MinecraftWindowWidth { get; set; } = 854;
        public int MinecraftWindowHeight { get; set; } = 480;
        public bool AutoSetChineseLanguage { get; set; } = true;
        public bool AutoSetJavaHighPerformanceGpu { get; set; } = true;
        public bool AutoOptimizeMemoryBeforeGameLaunch { get; set; }
        public string? OverrideMinecraftWindowTitle { get; set; }
        public string? JvmArgs { get; set; }
        public string? BeforeLaunchCommand { get; set; }
        public string? AfterLaunchCommand { get; set; }
        public string? PackagedCommand { get; set; }
    }

    public static int Run(PortalCommand command)
    {
        var settings = LoadSettings();
        var folders = LoadConfiguredFolders();

        var instance = ResolveLaunchInstance(command, folders);
        if (instance is null)
        {
            Write(string.IsNullOrWhiteSpace(command.Folder)
                ? string.Format(CommonLanguageManager.Instance.minecraft_instanceNotFound.CurrentValue(),
                    command.InstanceId)
                : string.Format(CommonLanguageManager.Instance.minecraft_instanceNotFoundInFolder.CurrentValue(),
                    command.Folder, command.InstanceId));
            return 1;
        }

        if (instance.Type == MinecraftInstanceType.Bedrock || instance.MinecraftEntry is not { } entry)
        {
            Write(CommonLanguageManager.Instance.launch_onlyJavaSupported.CurrentValue());
            return 1;
        }

        var loader = string.IsNullOrWhiteSpace(instance.LoaderDescription)
            ? CommonLanguageManager.Instance.minecraft_vanilla.CurrentValue()
            : instance.LoaderDescription;
        Write(string.Format(CommonLanguageManager.Instance.desktop_cli_launchFound.CurrentValue(),
            instance.InstanceName, instance.VersionId, loader));

        try
        {
            return LaunchJavaAsync(instance, entry, settings, command).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Write(string.Format(CommonLanguageManager.Instance.launch_failed.CurrentValue(),
                GetFailureReason(exception)));
            return 1;
        }
    }

    private static async Task<int> LaunchJavaAsync(MinecraftInstance instance, MinecraftEntry entry,
        CliSettings settings, PortalCommand command)
    {
        HttpUtil.Configure(false, null, "Portal/CLI");

        Write(CommonLanguageManager.Instance.launch_verifyingAccount.CurrentValue());
        var account = await VerifyAccountAsync(settings.UsingMinecraftMinecraftAccount);

        Write(CommonLanguageManager.Instance.launch_checkingJavaRuntime.CurrentValue());
        var reconcile = await JavaRuntimeManager.ReconcileAsync(settings.JavaRuntimes,
            settings.JavaVersionDefaultPaths);
        foreach (var message in JavaRuntimeManager.BuildMessages(reconcile))
            Write(message.Text);
        var java = await SelectJavaAsync(instance, settings);

        var options = BuildOptions(instance, settings);
        var placeholders = LaunchCustomization.BuildPlaceholders(instance, account, java, options);
        var extraGameArguments = new List<string>();
        var config = CreateLaunchConfig(instance, account, java, options, command, placeholders, extraGameArguments);

        await CompleteResourcesAsync(entry, options);

        Write(CommonLanguageManager.Instance.launch_startingProcess.CurrentValue());
        var mcProcess = await new Iridium.Launch.Launcher(
            resolver: new PortalGameArgumentParser(new StandardMinecraftArgumentParser(), extraGameArguments))
            .LaunchAsync(entry, config, CancellationToken.None);
        if (mcProcess.Process is not { } process)
            throw new InvalidOperationException(CommonLanguageManager.Instance.launch_noProcessInfo.CurrentValue());

        instance.Config.LastPlayTime = DateTime.Now;
        instance.Config.PlaySessions++;
        instance.SaveConfig();

        Write(string.Format(CommonLanguageManager.Instance.desktop_cli_launchProcessStarted.CurrentValue(),
            process.Id));

        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        mcProcess.OutputLogReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                Write(args.Data);
        };
        process.Exited += (_, _) =>
        {
            instance.SaveConfig();
            int code;
            try
            {
                code = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = 0;
            }

            exited.TrySetResult(code);
        };

        var exitCode = await exited.Task;
        Write(string.Format(CommonLanguageManager.Instance.desktop_cli_launchProcessExited.CurrentValue(), exitCode));
        return 0;
    }

    private static MinecraftInstance? ResolveLaunchInstance(PortalCommand command, List<MinecraftFolderEntry> folders)
    {
        var id = command.InstanceId?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (!string.IsNullOrWhiteSpace(command.Folder))
        {
            var configured = folders.FirstOrDefault(folder =>
                string.Equals(folder.FolderName, command.Folder, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(folder.FolderPath, command.Folder, StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
                return InstanceManager.Instance.ScanAll([configured]).FirstOrDefault(instance =>
                    MatchesInstanceId(instance, id));

            if (Directory.Exists(command.Folder))
            {
                var layout = MinecraftFolderLayout.Detect(command.Folder);
                if (layout.Kind == MinecraftFolderKind.Standard)
                {
                    try
                    {
                        var minecraftEntry = new StandardMinecraftProvider(new DirectoryInfo(layout.RootPath))
                            .GetMinecraftAsync(id).GetAwaiter().GetResult();
                        if (minecraftEntry is null)
                            return null;
                        return new MinecraftInstance(minecraftEntry);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

                var external = new MinecraftFolderEntry
                {
                    FolderName = Path.GetFileName(command.Folder.TrimEnd(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : command.Folder,
                    FolderPath = command.Folder,
                    FolderKind = MinecraftFolderKind.Auto
                };
                return InstanceManager.Instance.ScanAll([external]).FirstOrDefault(instance =>
                    MatchesInstanceId(instance, id));
            }

            return null;
        }

        return InstanceManager.Instance.ScanAll(folders).FirstOrDefault(instance => MatchesInstanceId(instance, id));
    }

    private static bool MatchesInstanceId(MinecraftInstance instance, string id)
    {
        return string.Equals(instance.MinecraftEntry?.Id, id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFileName(instance.InstanceFolderPath), id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(instance.InstanceName, id, StringComparison.OrdinalIgnoreCase);
    }

    private static CliSettings LoadSettings()
    {
        var settings = new CliSettings();
        var settingsPath = Portal.Core.Const.ConfigPath.SettingDataPath;
        if (!File.Exists(settingsPath))
            return settings;

        try
        {
            var loaded = JsonSerializer.Deserialize<CliSettings>(File.ReadAllText(settingsPath), PortalJson.Options)
                         ?? settings;
            loaded.JavaVersionDefaultPaths ??= new();
            return loaded;
        }
        catch (Exception)
        {
            return settings;
        }
    }

    private static List<MinecraftFolderEntry> LoadConfiguredFolders()
    {
        return CliInstanceScanner.LoadFolders()
            .Select(folder => new MinecraftFolderEntry
            {
                FolderName = folder.FolderName,
                FolderPath = folder.FolderPath,
                FolderKind = folder.FolderKind
            })
            .ToList();
    }

    private static async Task<Account> VerifyAccountAsync(MinecraftAccount? account)
    {
        if (account is null)
            throw new InvalidOperationException(CommonLanguageManager.Instance.launch_selectAccountFirst.CurrentValue());
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
                    throw new InvalidOperationException(
                        CommonLanguageManager.Instance.launch_yggdrasilIncomplete.CurrentValue());
                return new YggdrasilAccount(account.Name, account.Uuid.Value, account.AccessToken,
                    account.YggdrasilServerUrl, account.ClientToken) { MetaData = account.MetaData };
            case AccountType.Microsoft:
                var refreshed = await AccountRefresher.RefreshMicrosoft(account)
                                ?? throw new InvalidOperationException(
                                    CommonLanguageManager.Instance.launch_microsoftRefreshFailed.CurrentValue());
                if (!refreshed.Uuid.HasValue || string.IsNullOrWhiteSpace(refreshed.AccessToken) ||
                    string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                    throw new InvalidOperationException(
                        CommonLanguageManager.Instance.launch_microsoftMissingInfo.CurrentValue());
                return new MicrosoftAccount(refreshed.Name, refreshed.Uuid.Value, refreshed.AccessToken,
                    refreshed.RefreshToken, refreshed.LastRefreshTime ?? DateTime.Now);
            default:
                throw new InvalidOperationException(
                    CommonLanguageManager.Instance.launch_unsupportedAccountType.CurrentValue());
        }
    }

    private static async Task<JavaEntry> SelectJavaAsync(MinecraftInstance instance, CliSettings settings)
    {
        var entry = instance.MinecraftEntry!;
        var javaConfig = instance.JavaConfig;
        var preferred = javaConfig?.EnableSpecificJava == true ? javaConfig.SpecificJavaEntry : null;
        var requiredVersion = IridiumEntryHelper.GetAppropriateJavaVersion(entry);

        if (preferred is not null)
        {
            if (await JavaRuntimeVerifier.IsUsableAsync(preferred.JavaPath, preferred.MajorVersion))
                return ToJavaEntry(preferred);
            throw new MissingJavaVersionException(requiredVersion);
        }

        var candidates = settings.JavaRuntimes
            .Where(runtime => runtime.MajorVersion == requiredVersion)
            .ToList();
        if (settings.JavaVersionDefaultPaths.TryGetValue(requiredVersion, out var defaultPath) &&
            !string.IsNullOrWhiteSpace(defaultPath))
        {
            var match = candidates.FirstOrDefault(runtime =>
                string.Equals(runtime.JavaPath, defaultPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                candidates.Remove(match);
                candidates.Insert(0, match);
            }
        }

        foreach (var candidate in candidates)
        {
            if (await JavaRuntimeVerifier.IsUsableAsync(candidate.JavaPath, candidate.MajorVersion))
                return ToJavaEntry(candidate);
        }

        throw new MissingJavaVersionException(requiredVersion);
    }

    private static JavaEntry ToJavaEntry(JavaRuntimeEntry java)
    {
        return new JavaEntry
        {
            JavaPath = java.JavaPath,
            JavaHome = Path.GetDirectoryName(java.JavaPath) ?? string.Empty,
            Vendor = java.JavaType,
            Version = java.JavaVersion,
            MajorVersion = java.MajorVersion,
            Is64Bit = java.Is64Bit
        };
    }

    private static MinecraftLaunchOptions BuildOptions(MinecraftInstance instance, CliSettings settings)
    {
        var javaConfig = instance.JavaConfig;
        var overrideAdvanced = instance.Config.EnableOverrideAdvancedOptions;
        return new MinecraftLaunchOptions
        {
            Account = settings.UsingMinecraftMinecraftAccount,
            EnableGameOverlay = overrideAdvanced && javaConfig != null
                ? javaConfig.EnableGameOverlay
                : settings.EnableGameOverlay,
            IsFullscreen = overrideAdvanced && javaConfig != null
                ? javaConfig.EnableFullscreen
                : settings.EnableFullscreen,
            JavaRuntimes = settings.JavaRuntimes,
            JavaVersionDefaults = settings.JavaVersionDefaultPaths,
            WindowWidth = overrideAdvanced && javaConfig != null
                ? javaConfig.MinecraftWindowWidth
                : settings.MinecraftWindowWidth,
            WindowHeight = overrideAdvanced && javaConfig != null
                ? javaConfig.MinecraftWindowHeight
                : settings.MinecraftWindowHeight,
            MaxMemory = settings.MinecraftMaxMemory,
            AutoSetJavaHighPerformanceGpu = settings.AutoSetJavaHighPerformanceGpu,
            AutoOptimizeMemoryBeforeGameLaunch = settings.AutoOptimizeMemoryBeforeGameLaunch,
            SetChineseLanguageOnLaunch = overrideAdvanced && javaConfig != null
                ? javaConfig.AutoSetChineseLanguage
                : settings.AutoSetChineseLanguage,
            WindowTitle = overrideAdvanced && javaConfig != null &&
                          !string.IsNullOrWhiteSpace(javaConfig.OverrideMinecraftWindowTitle)
                ? javaConfig.OverrideMinecraftWindowTitle
                : settings.OverrideMinecraftWindowTitle,
            JvmArguments = overrideAdvanced && javaConfig != null && !string.IsNullOrWhiteSpace(javaConfig.JvmArgs)
                ? javaConfig.JvmArgs
                : settings.JvmArgs,
            WrapperCommand = overrideAdvanced && javaConfig != null &&
                             !string.IsNullOrWhiteSpace(javaConfig.PackagedCommand)
                ? javaConfig.PackagedCommand
                : settings.PackagedCommand
        };
    }

    private static LaunchConfig CreateLaunchConfig(MinecraftInstance instance, Account account, JavaEntry java,
        MinecraftLaunchOptions options, PortalCommand command, Dictionary<string, string> placeholders,
        List<string> extraGameArguments)
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
                : options.MaxMemory
        };

        ApplyGraphicsConfiguration(instance, config, extraGameArguments);

        if (!string.IsNullOrWhiteSpace(command.WorldFolder))
        {
            var worldFolder = command.WorldFolder.Trim();
            var savesPath = instance.GetSpecialFolder(MinecraftSpecialFolder.SavesFolder);
            if (!Directory.Exists(Path.Combine(savesPath, worldFolder)))
                throw new InvalidOperationException(string.Format(
                    CommonLanguageManager.Instance.minecraft_worldFolderNotFound.CurrentValue(),
                    instance.InstanceName, worldFolder));
            config.SaveName = worldFolder;
        }

        if (!string.IsNullOrWhiteSpace(command.ServerAddress))
        {
            config.ServerInfo = new ServerInfo
            {
                Address = command.ServerAddress.Trim(),
                Port = command.ServerPort ?? 25565
            };
        }

        config.IsFullscreen = options.IsFullscreen;
        if (options.IsFullscreen)
        {
            config.Width = 0;
            config.Height = 0;
        }

        return config;
    }

    private static void ApplyGraphicsConfiguration(MinecraftInstance instance, LaunchConfig config,
        List<string> extraGameArguments)
    {
        var entry = instance.MinecraftEntry;
        if (entry is null)
            return;

        try
        {
            var version = GameVersion.Parse(entry.MinecraftVersion);
            var graphics = instance.JavaConfig?.GraphicsBackend ?? GraphicsApi.Default;
            var effective = GraphicsEnvironmentResolver.Resolve(graphics,
                instance.JavaConfig?.OpenGlRenderer, instance.JavaConfig?.VulkanRenderer, version);
            var nativesFolder = string.IsNullOrEmpty(config.NativesFolder)
                ? IridiumEntryHelper.GetNativesDirectory(entry)
                : config.NativesFolder;
            var launch = GraphicsLaunchArgumentsBuilder.Build(effective, graphics, version, nativesFolder,
                Renderers.CurrentPlatform);

            if (launch.EnvironmentVariables.Count > 0)
                config.EnvironmentVariables = new Dictionary<string, string>(launch.EnvironmentVariables);
            if (launch.GameArguments.Any())
                extraGameArguments.AddRange(launch.GameArguments);
            if (launch.NeedsMesaAgent)
                config.JvmArguments = config.JvmArguments.Concat(launch.JvmArguments);
        }
        catch (Exception exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.launchCustomization_backgroundCommandFailed.CurrentValue(),
                Environment.NewLine, exception));
        }
    }

    private static async Task CompleteResourcesAsync(MinecraftEntry entry, MinecraftLaunchOptions options)
    {
        Write(CommonLanguageManager.Instance.launch_checkingResources.CurrentValue());

        var copies = MinecraftResourceCompleter.BuildCopies(entry, options.ResourceSourceRoots);
        Write(string.Format(CommonLanguageManager.Instance.launch_resourcesToProcess.CurrentValue(),
            copies.Count, 0));

        await MinecraftResourceCompleter.CopyAsync(copies, null, CancellationToken.None);

        var result = await MinecraftResourceCompleter.DownloadAsync(entry, null, CancellationToken.None);
        if (result.FailCount > 0)
            throw new IOException(string.Format(
                CommonLanguageManager.Instance.launch_resourceCompletionFailed.CurrentValue(),
                result.FailCount));

        Write(CommonLanguageManager.Instance.launch_resourcesCompleted.CurrentValue());
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

    private static void Write(params string[] lines)
    {
        PortalCommandService.WriteConsoleLines(lines);
    }
}
