using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
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
        else
        {
            Log(BedrockLogLevel.Warning, "未收到基岩版 Xbox 账户，跳过登录注入；请启用基岩版账户注入并选择账户");
        }
        await EnsureGameInputAsync(runtime, cancellationToken).ConfigureAwait(false);
        await EnsureGamePatchAsync(executablePath, cancellationToken).ConfigureAwait(false);
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

    private static string FormatBytes(double bytes)
    {
        var units = new[] { "B", "KiB", "MiB", "GiB" };
        var unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes:F0} {units[unit]}" : $"{bytes:F1} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours}小时{duration.Minutes:D2}分"
        : duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}分{duration.Seconds:D2}秒"
            : $"{Math.Max(1, (int)duration.TotalSeconds)}秒";

    private async Task EnsureGamePatchAsync(string executablePath, CancellationToken cancellationToken)
    {
        var instancePath = Path.GetDirectoryName(executablePath)!;
        var preload = Path.Combine(instancePath, "preload");
        var patch = Path.Combine(preload, "mcpatcher_core.dll");
        if (File.Exists(patch)) return;

        const string url = "https://github.com/RoundMCDev/ProtonGDK-Release/releases/download/Release10-32/GamePatch.zip";
        var cacheRoot = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cacheRoot))
            cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        var archivePath = Path.Combine(cacheRoot, "Portal", "Bedrock", "GamePatch.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

        try
        {
            if (!File.Exists(archivePath))
            {
                Log(BedrockLogLevel.Information, "正在下载基岩版窗口兼容补丁");
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Portal-Bedrock-Linux/1.0");
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var output = new FileStream(archivePath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1024 * 64];
                long downloadedBytes = 0;
                var stopwatch = Stopwatch.StartNew();
                var lastReport = TimeSpan.Zero;
                var lastLoggedPercentage = -1;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloadedBytes += read;
                    if (stopwatch.Elapsed - lastReport < TimeSpan.FromMilliseconds(250) &&
                        !(totalBytes > 0 && downloadedBytes == totalBytes)) continue;

                    var speed = stopwatch.Elapsed.TotalSeconds > 0
                        ? downloadedBytes / stopwatch.Elapsed.TotalSeconds
                        : 0;
                    var percentage = totalBytes > 0
                        ? downloadedBytes * 100d / totalBytes
                        : (double?)null;
                    TimeSpan? remaining = speed > 0 && totalBytes > 0
                        ? TimeSpan.FromSeconds(Math.Max(0, totalBytes - downloadedBytes) / speed)
                        : null;
                    var text = totalBytes > 0
                        ? $"下载窗口兼容补丁 {percentage:F1}% ({FormatBytes(downloadedBytes)}/{FormatBytes(totalBytes)})，速度 {FormatBytes(speed)}/s" +
                          (remaining is { } time ? $"，剩余 {FormatDuration(time)}" : string.Empty)
                        : $"下载窗口兼容补丁 ({FormatBytes(downloadedBytes)})，速度 {FormatBytes(speed)}/s";
                    UpdateProgress?.Invoke($"状态：{text}", percentage);
                    var integerPercentage = totalBytes > 0 ? (int)percentage!.Value : -1;
                    if (integerPercentage != lastLoggedPercentage)
                    {
                        Log(BedrockLogLevel.Information, text);
                        lastLoggedPercentage = integerPercentage;
                    }
                    lastReport = stopwatch.Elapsed;
                }
                UpdateProgress?.Invoke("状态：窗口兼容补丁下载完成", 100);
            }

            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.Entries.FirstOrDefault(item =>
                item.FullName.Replace('\\', '/').Equals("gdk/mcpatcher_core.dll",
                    StringComparison.OrdinalIgnoreCase));
            if (entry is null) throw new InvalidDataException("GamePatch.zip 中缺少 gdk/mcpatcher_core.dll。");

            Directory.CreateDirectory(preload);
            await using var source = entry.Open();
            await using var destination = new FileStream(patch, FileMode.Create, FileAccess.Write,
                FileShare.Read, 1024 * 64, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            Log(BedrockLogLevel.Information, "基岩版窗口兼容补丁已部署");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log(BedrockLogLevel.Warning, $"窗口兼容补丁不可用，继续启动游戏：{exception.Message}");
        }
    }

    private async Task EnsureGameInputAsync(LinuxBedrockRuntime runtime, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsLinux())
        {
            await InstallGameInputOfflineAsync(runtime, cancellationToken).ConfigureAwait(false);
            return;
        }

        var installer = Path.Combine(_instanceConfig.InstancePath, "Installers", "GameInputRedist.msi");
        if (!File.Exists(installer)) return;

        var marker = Path.Combine(runtime.PrefixPath, ".portal-gameinput-installed");
        if (File.Exists(marker) || HasGameInput(runtime.PrefixPath)) return;

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
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
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

            Log(BedrockLogLevel.Warning, "GameInput 安装超时，安装进程已被终止；继续启动游戏");
            UpdateProgress?.Invoke("状态：GameInput 安装超时，继续启动游戏", null);
            return;
        }
        var errorText = errorBuffer.ToString().Trim();
        if (process.ExitCode != 0)
        {
            Log(BedrockLogLevel.Warning,
                $"GameInput 安装失败（退出码 {process.ExitCode}），继续启动游戏。{errorText}");
            UpdateProgress?.Invoke("状态：GameInput 安装失败，继续启动游戏", null);
            return;
        }

        Directory.CreateDirectory(runtime.PrefixPath);
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
        UpdateProgress?.Invoke("状态：GameInput 运行组件安装完成", 100);
        Log(BedrockLogLevel.Information, "GameInput 运行组件安装完成");
    }

    private async Task InstallGameInputOfflineAsync(LinuxBedrockRuntime runtime,
        CancellationToken cancellationToken)
    {
        var installer = Path.Combine(_instanceConfig.InstancePath, "Installers", "GameInputRedist.msi");
        if (!File.Exists(installer))
        {
            Log(BedrockLogLevel.Warning, "未找到 GameInputRedist.msi，无法安装 GameInput 运行组件");
            return;
        }

        var marker = Path.Combine(runtime.PrefixPath, ".portal-gameinput-installed-v2");
        if (File.Exists(marker) && HasGameInput(runtime.PrefixPath)) return;

        cancellationToken.ThrowIfCancellationRequested();
        Log(BedrockLogLevel.Information, "正在离线提取 GameInput 运行组件");
        UpdateProgress?.Invoke("状态：正在安装 GameInput", null);
        InstallCryptbase(runtime);
        var cab = ExtractEmbeddedCab(installer);
        if (cab is null) throw new InvalidDataException("GameInput MSI 中没有内嵌 CAB。");
        var extractor = Path.Combine(runtime.ProtonRoot, "protonfixes", "files", "bin", "cabextract");
        if (!File.Exists(extractor)) throw new FileNotFoundException("当前 Proton 缺少 cabextract。", extractor);

        var temp = Path.Combine(Path.GetTempPath(), $"portal-gameinput-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var cabPath = Path.Combine(temp, "GameInput.cab");
            await File.WriteAllBytesAsync(cabPath, cab, cancellationToken).ConfigureAwait(false);
            var extract = new ProcessStartInfo(extractor, $"-q -d \"{temp}\" \"{cabPath}\"")
            {
                UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true
            };
            using var process = Process.Start(extract) ?? throw new InvalidOperationException("无法启动 cabextract。");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidDataException($"GameInput CAB 提取失败：{await process.StandardError.ReadToEndAsync(cancellationToken)}");

            var dlls = Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories)
                .Where(path => IsPe(path, dll: true)).OrderByDescending(path => new FileInfo(path).Length).ToList();
            var exes = Directory.EnumerateFiles(temp, "*", SearchOption.AllDirectories)
                .Where(path => IsPe(path, dll: false)).OrderByDescending(path => new FileInfo(path).Length).ToList();
            if (dlls.Count == 0 || exes.Count == 0) throw new InvalidDataException("GameInput CAB 中没有有效组件。");

            var x64 = Path.Combine(runtime.PrefixPath, "pfx", "drive_c", "Program Files", "Microsoft GameInput", "x64");
            var x86 = Path.Combine(runtime.PrefixPath, "pfx", "drive_c", "Program Files", "Microsoft GameInput", "x86");
            var system32 = Path.Combine(runtime.PrefixPath, "pfx", "drive_c", "windows", "system32");
            Directory.CreateDirectory(x64); Directory.CreateDirectory(system32);
            File.Copy(dlls[0], Path.Combine(x64, "GameInputRedist.dll"), true);
            File.Copy(dlls[0], Path.Combine(system32, "GameInputRedist.dll"), true);
            File.Copy(exes[0], Path.Combine(x64, "GameInputRedistService.exe"), true);
            if (dlls.Count > 1) File.Copy(dlls[1], Path.Combine(x64, "GameInputBridge.dll"), true);
            if (exes.Count > 1) File.Copy(exes[1], Path.Combine(x64, "GameInputRawInputProxy.exe"), true);
            if (dlls.Count > 2)
            {
                Directory.CreateDirectory(x86);
                File.Copy(dlls[2], Path.Combine(x86, "GameInputRedist.dll"), true);
            }
            await RegisterGameInputServiceAsync(runtime, cancellationToken).ConfigureAwait(false);
        }
        finally { Directory.Delete(temp, true); }

        Directory.CreateDirectory(runtime.PrefixPath);
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O"), cancellationToken)
            .ConfigureAwait(false);
        UpdateProgress?.Invoke("状态：GameInput 运行组件安装完成", 100);
        Log(BedrockLogLevel.Information, "GameInput 运行组件安装完成");
    }

    private static bool IsPe(string path, bool dll)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < 0x40) return false;
        var header = new byte[0x40]; stream.ReadExactly(header);
        if (header[0] != 'M' || header[1] != 'Z') return false;
        stream.Position = BitConverter.ToInt32(header, 0x3c);
        var pe = new byte[24]; stream.ReadExactly(pe);
        return pe[0] == 'P' && pe[1] == 'E' && ((BitConverter.ToUInt16(pe, 22) & 0x2000) != 0) == dll;
    }

    private async Task RegisterGameInputServiceAsync(LinuxBedrockRuntime runtime, CancellationToken cancellationToken)
    {
        var reg = Path.Combine(runtime.PrefixPath, $"portal-gameinput-{Guid.NewGuid():N}.reg");
        var redist = @"C:\Program Files\Microsoft GameInput\x64";
        var service = @"System\ControlSet001\Services\GameInputRedistService";
        await File.WriteAllTextAsync(reg,
            "Windows Registry Editor Version 5.00\n\n" +
            "[HKEY_LOCAL_MACHINE\\Software\\Microsoft\\GameInput]\n" +
            $"\"RedistDir\"=\"{redist.Replace("\\", "\\\\")}\"\n\n" +
            $"[HKEY_LOCAL_MACHINE\\{service}]\n" +
            "\"DisplayName\"=\"GameInput Redist Service\"\n" +
            "\"Description\"=\"GameInput Redist Service\"\n" +
            $"\"ImagePath\"=\"{redist.Replace("\\", "\\\\")}\\\\GameInputRedistService.exe\"\n" +
            "\"ObjectName\"=\"LocalSystem\"\n\"ErrorControl\"=dword:00000000\n\"Start\"=dword:00000003\n\"Type\"=dword:00000010\n", cancellationToken);
        try
        {
            var info = new ProcessStartInfo(runtime.ProtonScript, $"run reg import \"{ToWinePath(reg)}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            ApplyRuntimeEnvironment(info, runtime);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("无法注册 GameInput 服务。");
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("GameInput 服务注册失败。");
        }
        finally { File.Delete(reg); }
    }

    private static byte[]? ExtractEmbeddedCab(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 8 || data[0] != 0xd0 || data[1] != 0xcf) return null;
        var sectorSize = 1 << BitConverter.ToUInt16(data, 0x1e);
        var directory = BitConverter.ToInt32(data, 0x30);
        var miniCutoff = BitConverter.ToInt32(data, 0x38);
        const int End = -2, Free = -1;
        byte[] Sector(int n) => data.AsSpan((n + 1) * sectorSize, sectorSize).ToArray();
        var difat = Enumerable.Range(0, 109).Select(i => BitConverter.ToInt32(data, 0x4c + i * 4)).Where(x => x != Free).ToList();
        var next = BitConverter.ToInt32(data, 0x44);
        for (var i = 0; i < BitConverter.ToInt32(data, 0x48) && next != End && next != Free; i++) { var s = Sector(next); difat.AddRange(Enumerable.Range(0, sectorSize / 4 - 1).Select(j => BitConverter.ToInt32(s, j * 4))); next = BitConverter.ToInt32(s, sectorSize - 4); }
        var fat = difat.SelectMany(n => Enumerable.Range(0, sectorSize / 4).Select(i => BitConverter.ToInt32(Sector(n), i * 4))).ToArray();
        List<int> Chain(int start) { var result = new List<int>(); var seen = new HashSet<int>(); for (var n = start; n != End && n != Free && n >= 0 && n < fat.Length && seen.Add(n); n = fat[n]) result.Add(n); return result; }
        byte[] ReadBig(int start, int size) { var result = new byte[size]; var offset = 0; foreach (var n in Chain(start)) { var part = Sector(n); var count = Math.Min(part.Length, size - offset); Buffer.BlockCopy(part, 0, result, offset, count); offset += count; } return result; }
        var dir = ReadBig(directory, Chain(directory).Count * sectorSize);
        for (var i = 0; i + 128 <= dir.Length; i += 128) if (dir[i + 66] == 2) { var start = BitConverter.ToInt32(dir, i + 116); var size = (int)BitConverter.ToInt64(dir, i + 120); if (size >= 4 && size >= miniCutoff) { var stream = ReadBig(start, size); if (stream[0] == 'M' && stream[1] == 'S' && stream[2] == 'C' && stream[3] == 'F') return stream; } }
        return null;
    }

    private static void InstallCryptbase(LinuxBedrockRuntime runtime)
    {
        var source = Path.Combine(runtime.ProtonRoot, "files", "lib", "wine", "x86_64-windows", "cryptbase.dll");
        var destination = Path.Combine(runtime.PrefixPath, "pfx", "drive_c", "windows", "system32", "cryptbase.dll");
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    private static bool HasGameInput(string prefixPath)
    {
        var x64 = Path.Combine(prefixPath, "pfx", "drive_c", "Program Files", "Microsoft GameInput", "x64");
        var system32 = Path.Combine(prefixPath, "pfx", "drive_c", "windows", "system32");
        return File.Exists(Path.Combine(x64, "GameInputRedist.dll")) &&
               File.Exists(Path.Combine(x64, "GameInputRedistService.exe")) &&
               File.Exists(Path.Combine(system32, "GameInputRedist.dll"));
    }

    private static void ApplyRuntimeEnvironment(ProcessStartInfo startInfo, LinuxBedrockRuntime runtime)
    {
        startInfo.Environment["STEAM_COMPAT_DATA_PATH"] = runtime.PrefixPath;
        startInfo.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = runtime.SteamCompatPath;
        startInfo.Environment["LD_LIBRARY_PATH"] = BuildLibraryPath(runtime.ProtonRoot);
        startInfo.Environment["WINEDLLOVERRIDES"] = "dxgi,d3d11,d3d10core,d3d9=b";
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
