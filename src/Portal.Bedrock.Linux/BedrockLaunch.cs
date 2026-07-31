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

    public override async Task Launch()
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
            UpdateProgress?.Invoke($"状态：{runtimeProgress.Message}", runtimeProgress.Percentage);
        }).ConfigureAwait(false);
        await EnsureGameInputAsync(runtime).ConfigureAwait(false);
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
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"] = runtime.PrefixPath;
        startInfo.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = runtime.SteamClientPath;
        startInfo.Environment["LD_LIBRARY_PATH"] = BuildLibraryPath(runtime.ProtonRoot);
        startInfo.Environment["WINEDLLOVERRIDES"] = "dxgi,d3d11,d3d10core,d3d9=b";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Information);
        process.ErrorDataReceived += (_, args) => ForwardLog(args.Data, BedrockLogLevel.Error);

        Log(BedrockLogLevel.Information,
            $"使用 Proton 启动 GDK 实例；runtime={runtime.ProtonRoot}，prefix={runtime.PrefixPath}");
        if (!process.Start()) throw new InvalidOperationException("Proton 进程未能启动。");

        MinecraftProcess = process;
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

    private async Task EnsureGameInputAsync(LinuxBedrockRuntime runtime)
    {
        var installer = Path.Combine(_instanceConfig.InstancePath, "Installers", "GameInputRedist.msi");
        if (!File.Exists(installer)) return;

        var marker = Path.Combine(runtime.PrefixPath, ".portal-gameinput-installed");
        if (File.Exists(marker)) return;

        Log(BedrockLogLevel.Information, "正在通过 Proton 安装 GameInput 运行组件");
        UpdateProgress?.Invoke("状态：正在安装 GameInput", 0);
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 GameInput 安装程序。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var outputText = await output.ConfigureAwait(false);
        var errorText = await error.ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(outputText)) Log(BedrockLogLevel.Debug, outputText.Trim());
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"GameInput 安装失败（退出码 {process.ExitCode}）：{errorText.Trim()}");

        Directory.CreateDirectory(runtime.PrefixPath);
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
        Log(BedrockLogLevel.Information, "GameInput 运行组件安装完成");
    }

    private static void ApplyRuntimeEnvironment(ProcessStartInfo startInfo, LinuxBedrockRuntime runtime)
    {
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"] = runtime.PrefixPath;
        startInfo.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = runtime.SteamClientPath;
        startInfo.Environment["LD_LIBRARY_PATH"] = BuildLibraryPath(runtime.ProtonRoot);
        startInfo.Environment["WINEDLLOVERRIDES"] = "dxgi,d3d11,d3d10core,d3d9=b";
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
