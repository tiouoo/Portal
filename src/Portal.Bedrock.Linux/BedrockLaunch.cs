using System.Diagnostics;
using Portal.Bedrock.Standard.Interface;
using Portal.Bedrock.Standard.Manifest;

namespace Portal.Bedrock.Linux;

public sealed class BedrockLaunch : IBedrockLaunch
{
    private readonly BedrockInstanceConfig _instanceConfig;
    private readonly LinuxBedrockRuntimeResolver _runtimeResolver;

    public BedrockLaunch(BedrockInstanceConfig instanceConfig,
        LinuxBedrockRuntimeResolver? runtimeResolver = null)
    {
        _instanceConfig = instanceConfig ?? throw new ArgumentNullException(nameof(instanceConfig));
        _runtimeResolver = runtimeResolver ?? new LinuxBedrockRuntimeResolver();
    }

    public override async Task Launch(CancellationToken cancellationToken)
    {
        LinuxBedrockRuntimeResolver.EnsureSupportedPlatform();
        if (_instanceConfig.BuildType != BedrockBuildType.GDK)
            throw new PlatformNotSupportedException("Linux 平台仅支持 GDK 构建；UWP 无法通过此启动器运行。");

        var executablePath = Path.GetFullPath(Path.Combine(_instanceConfig.InstancePath, "Minecraft.Windows.exe"));
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("实例中缺少 Minecraft.Windows.exe，请先安装 GDK x64 版本。", executablePath);

        var runtime = await _runtimeResolver.ResolveAsync(runtimeProgress =>
        {
            Log(BedrockLogLevel.Information, runtimeProgress.Message +
                (runtimeProgress.TotalBytes > 0 ? $" ({runtimeProgress.Percentage}%)" : string.Empty));
            UpdateProgress?.Invoke($"状态：{runtimeProgress.Message}", runtimeProgress.TotalBytes > 0
                ? runtimeProgress.Percentage
                : null);
        }, cancellationToken).ConfigureAwait(false);
        string? preauthDevice = null;
        if (Authentication != null)
        {
            Log(BedrockLogLevel.Information, $"正在为 Xbox 账户 {Authentication.Gamertag} 准备 WineGDK 预认证");
            preauthDevice = await new XboxPreauthService(runtime.PrefixPath)
                .PrepareAsync(Authentication, cancellationToken).ConfigureAwait(false);
            await SetRefreshTokenAsync(runtime, Authentication.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        await EnsureGameInputAsync(runtime, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ProtonScript,
            WorkingDirectory = _instanceConfig.InstancePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(executablePath);
        foreach (var argument in ParseArguments(_instanceConfig.LaunchArguments))
            startInfo.ArgumentList.Add(argument);
        if (_instanceConfig.EnableCreatorEditor)
            startInfo.ArgumentList.Add("minecraft://creator/?Editor=true");
        ApplyRuntimeEnvironment(startInfo, runtime);
        if (preauthDevice != null)
            startInfo.Environment["WINEGDK_PREAUTH_DEVICE"] = ToWinePath(preauthDevice);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Information);
        process.ErrorDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Error);

        Log(BedrockLogLevel.Information,
            $"使用 Proton 启动 GDK 实例；runtime={runtime.ProtonRoot}，prefix={runtime.PrefixPath}");
        if (!process.Start()) throw new InvalidOperationException("Proton 进程未能启动。");

        MinecraftProcess = process;
        ProcessStarted?.Invoke(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Log(BedrockLogLevel.Information, $"Proton 已启动，PID：{process.Id}");
        UpdateProgress?.Invoke("状态：游戏启动命令已提交", 100);
        LaunchFinish?.Invoke();
    }

    public override Process GetProcess() => MinecraftProcess ?? throw new InvalidOperationException("游戏尚未启动。");

    private static string BuildLibraryPath(string protonRoot)
    {
        var entries = new[]
        {
            Path.Combine(protonRoot, "files", "lib64"),
            Path.Combine(protonRoot, "files", "lib"),
            Environment.GetEnvironmentVariable("LD_LIBRARY_PATH")
        };
        return string.Join(Path.PathSeparator, entries.Where(entry => !string.IsNullOrWhiteSpace(entry)));
    }

    private void ForwardLog(string? message, BedrockLogLevel level)
    {
        if (!string.IsNullOrEmpty(message)) Log(level, message);
    }

    private void Log(BedrockLogLevel level, string message) => LogReceived?.Invoke(message, level);

    private async Task EnsureGameInputAsync(LinuxBedrockRuntime runtime, CancellationToken cancellationToken)
    {
        var installer = Path.Combine(_instanceConfig.InstancePath, "Installers", "GameInputRedist.msi");
        if (!File.Exists(installer)) return;

        var marker = Path.Combine(runtime.PrefixPath, ".portal-gameinput-installed");
        if (File.Exists(marker)) return;

        cancellationToken.ThrowIfCancellationRequested();
        Log(BedrockLogLevel.Information, "正在通过 Proton 安装 GameInput 运行组件");
        UpdateProgress?.Invoke("状态：正在安装 GameInput", null);
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ProtonScript,
            WorkingDirectory = runtime.ProtonRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("msiexec");
        startInfo.ArgumentList.Add("/i");
        startInfo.ArgumentList.Add(installer);
        startInfo.ArgumentList.Add("/qn");
        ApplyRuntimeEnvironment(startInfo, runtime);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var errorBuffer = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;
            Log(BedrockLogLevel.Information, args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.Data)) return;
            errorBuffer.AppendLine(args.Data);
            Log(BedrockLogLevel.Warning, args.Data);
        };
        if (!process.Start()) throw new InvalidOperationException("无法启动 GameInput 安装程序。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        using var cancellation = timeout.Token.Register(() => KillProcess(process));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            if (cancellationToken.IsCancellationRequested)
            {
                Log(BedrockLogLevel.Warning, "GameInput 安装已取消，安装进程已被终止");
                UpdateProgress?.Invoke("状态：GameInput 安装已取消", null);
                throw;
            }

            Log(BedrockLogLevel.Error, "GameInput 安装超时，安装进程已被终止");
            UpdateProgress?.Invoke("状态：GameInput 安装超时", null);
            throw new TimeoutException("GameInput 安装超时（10 分钟），请检查 Wine/Proton 环境后重试。");
        }
        var errorText = errorBuffer.ToString().Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"GameInput 安装失败（退出码 {process.ExitCode}）：{errorText}");

        Directory.CreateDirectory(runtime.PrefixPath);
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
        UpdateProgress?.Invoke("状态：GameInput 运行组件安装完成", 100);
        Log(BedrockLogLevel.Information, "GameInput 运行组件安装完成");
    }

    private static void ApplyRuntimeEnvironment(ProcessStartInfo startInfo, LinuxBedrockRuntime runtime)
    {
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"] = runtime.PrefixPath;
        startInfo.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = runtime.SteamCompatPath;
        startInfo.Environment["LD_LIBRARY_PATH"] = BuildLibraryPath(runtime.ProtonRoot);
        startInfo.Environment["WINEDLLOVERRIDES"] = "dxgi,d3d11,d3d10core,d3d9=b";
        // 使用内置 WoW64 运行时（files/bin-wow64），避免依赖宿主机 32 位运行库
        // （/lib/ld-linux.so.2）。32 位组件（如 msiexec / GameInput 安装）无需
        // 宿主机安装 32 位 multilib 也能运行。
        startInfo.Environment["PROTON_USE_WOW64"] = "1";
    }

    private async Task SetRefreshTokenAsync(LinuxBedrockRuntime runtime, string refreshToken,
        CancellationToken cancellationToken)
    {
        var registryFile = Path.Combine(runtime.PrefixPath, $"portal-xbox-{Guid.NewGuid():N}.reg");
        Directory.CreateDirectory(runtime.PrefixPath);
        await File.WriteAllTextAsync(registryFile,
            "Windows Registry Editor Version 5.00\n\n[HKEY_LOCAL_MACHINE\\Software\\Wine\\WineGDK]\n" +
            $"\"RefreshToken\"=\"{EscapeRegistryValue(refreshToken)}\"\n", cancellationToken).ConfigureAwait(false);
        try { File.SetUnixFileMode(registryFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch (PlatformNotSupportedException) { }
        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ProtonScript,
            WorkingDirectory = runtime.ProtonRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("reg");
        startInfo.ArgumentList.Add("import");
        startInfo.ArgumentList.Add(ToWinePath(registryFile));
        ApplyRuntimeEnvironment(startInfo, runtime);
        try
        {
            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("无法写入 WineGDK 账户配置。");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("写入 WineGDK 账户配置失败。");
            Log(BedrockLogLevel.Information, "Xbox 刷新令牌已写入 WineGDK machine registry");
        }
        finally
        {
            File.Delete(registryFile);
        }
    }

    private static string ToWinePath(string path) => $"Z:{Path.GetFullPath(path).Replace('/', '\\')}";
    private static string EscapeRegistryValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static IEnumerable<string> ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) yield break;
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in arguments)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length == 0) continue;
                yield return current.ToString();
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        if (quoted) throw new FormatException("基岩版启动参数包含未闭合的双引号。");
        if (current.Length > 0) yield return current.ToString();
    }
}
