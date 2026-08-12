using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Utilities;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Module.Update;

public static class UpdateApp
{
    private const int RetainedUpdateDirectories = 1;

    public sealed record PreparedUpdate(ProcessStartInfo StartInfo, bool RunsInstaller, bool WaitForStart = false);

    private sealed class UpdateTaskHandle
    {
        public required ManagedTask Task { get; init; }
        public PreparedUpdate? PreparedUpdate { get; set; }
    }

    public static async Task<PreparedUpdate?> Prepare(TopLevel sender)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info("开始检查并准备应用更新。");
        try
        {
            var release = await UpdateChecker.GetRelease();
            if (!UpdateChecker.IsNewer(release))
            {
                sender.Notice("当前是最新版本", NotificationType.Success);
                return null;
            }

            var packageType = Data.Instance.PackageType.Trim().ToLowerInvariant();
            var asset = SelectAsset(release, packageType);
            asset = await UpdateChecker.ResolveDownloadMetadata(asset);
            var updateDirectory = Path.Combine(ConfigPath.UpdateFolderPath, release.Sequence > 0
                ? release.Sequence.ToString()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            Directory.CreateDirectory(updateDirectory);
            CleanupOldUpdateDirectories(updateDirectory);
            var packagePath = Path.Combine(updateDirectory, asset.Name);
            Logger.Info($"已选择更新包：{asset.Name}，下载目录：{updateDirectory}");

            sender.Notice($"正在下载 {asset.Name}", NotificationType.Information);
            var taskHandle = await Download(asset, packagePath);

            // 解压与外部进程等待都是阻塞操作，放到线程池执行，避免大包冻结 UI
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && packageType == "installer")
            {
                var installerUpdate = new PreparedUpdate(
                    await Task.Run(() => PrepareWindowsInstaller(packagePath, updateDirectory)), true);
                CompletePreparation(taskHandle, installerUpdate);
                return installerUpdate;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && packageType is "deb" or "rpm")
            {
                var processPath = Environment.ProcessPath
                                  ?? throw new InvalidOperationException("无法确定当前程序路径。");
                var packageUpdate = new PreparedUpdate(
                    await Task.Run(() => PrepareLinuxPackage(packagePath, updateDirectory, packageType, processPath)), true, true);
                CompletePreparation(taskHandle, packageUpdate);
                return packageUpdate;
            }

            var path = Environment.ProcessPath
                              ?? throw new InvalidOperationException("无法确定当前程序路径。");
            ProcessStartInfo updater;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && packageType == "portable")
                updater = await Task.Run(() => PrepareWindowsPortable(packagePath, updateDirectory, path));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && packageType == "appimage")
                updater = await Task.Run(() => PrepareAppImage(packagePath, updateDirectory));
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && packageType is "app" or "dmg")
                updater = await Task.Run(() => PrepareMacApp(packagePath, updateDirectory, path));
            else
                throw new NotSupportedException($"当前系统不支持安装类型“{packageType}”的自动更新。");

            var preparedUpdate = new PreparedUpdate(updater, false);
            CompletePreparation(taskHandle, preparedUpdate);
            Logger.Info($"应用更新准备完成，耗时 {stopwatch.ElapsedMilliseconds} ms。");
            return preparedUpdate;
        }
        catch (Exception ex)
        {
            Logger.Error("准备应用更新失败。", ex);
            sender.Notice($"更新失败：{ex.Message}", NotificationType.Error);
            return null;
        }
    }

    public static async Task Apply(PreparedUpdate update)
    {
        if (!await ApplicationEvents.RaiseAppExiting()) return;
        App.Method.FlushConfig();
        using var process = Process.Start(update.StartInfo)
                            ?? throw new InvalidOperationException("无法启动更新安装程序。");
        if (update.WaitForStart)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"更新安装程序未能启动（退出代码 {process.ExitCode}）。");
        }
        Environment.Exit(0);
    }

    internal static UpdateAsset SelectAsset(UpdateRelease release, string packageType)
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => throw new PlatformNotSupportedException($"不支持 {RuntimeInformation.ProcessArchitecture} 架构更新。")
        };
        string expectedName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (arch != "x64") throw new PlatformNotSupportedException("当前没有 Windows ARM 更新包。");
            expectedName = packageType switch
            {
                "installer" => "Portal.win.x64.installer.zip",
                "portable" => "Portal.win.x64.portable.zip",
                _ => throw new NotSupportedException($"无法自动更新 Windows 安装类型“{packageType}”。")
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (arch != "x64") throw new PlatformNotSupportedException("Portal Linux 版本目前仅发布 x64 更新包。");
            expectedName = packageType switch
            {
                "appimage" => "Portal.linux.x64.AppImage",
                "deb" => "Portal.linux.x64.deb",
                "rpm" => "Portal.linux.x64.rpm",
                _ => throw new NotSupportedException($"无法自动更新 Linux 安装类型“{packageType}”。")
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (packageType is not ("app" or "dmg")) throw new NotSupportedException("macOS 自动更新仅支持应用程序包。");
            if (arch == "arm") throw new PlatformNotSupportedException("不支持 32 位 ARM macOS。");
            expectedName = $"Portal.osx.mac.{arch}.app.zip";
        }
        else
        {
            throw new PlatformNotSupportedException("当前操作系统不支持自动更新。");
        }

        return release.Assets.SingleOrDefault(asset => asset.Name.Equals(expectedName, StringComparison.Ordinal))
               ?? throw new FileNotFoundException($"发布中找不到匹配的更新包：{expectedName}");
    }

    private static async Task<UpdateTaskHandle> Download(UpdateAsset asset, string destination)
    {
        // GitHub 镜像仅改写 GitHub 下载地址；CNB 保持其平台直链。
        var downloadUrl = GithubMirror.Apply(asset.DownloadUrl);
        if (!downloadUrl.Equals(asset.DownloadUrl, StringComparison.Ordinal))
            Logger.Info($"Downloading update via GitHub mirror: {downloadUrl}");
        var temporary = destination + ".download";
        UpdateTaskHandle? handle = null;
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = "下载 Portal 更新",
            Description = $"正在连接：{asset.Name}",
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = "取消下载",
                    Description = "取消 Portal 更新包下载",
                    IconKey = "Cancel",
                    ExecuteAsync = (managedTask, _) =>
                    {
                        managedTask.RequestCancellation();
                        return Task.CompletedTask;
                    },
                    CanExecute = managedTask => managedTask.CanBeCancelled,
                    IsVisible = managedTask => !managedTask.IsTerminal
                },
                new TaskActionDefinition
                {
                    Name = "更新并重启",
                    Description = "退出 Portal，应用已下载的更新并重新启动",
                    IconKey = "Refresh",
                    ExecuteAsync = async (_, _) =>
                    {
                        if (handle?.PreparedUpdate is { } update) await Apply(update);
                    },
                    CanExecute = managedTask => managedTask.Status == ManagedTaskStatus.Completed
                                                && handle?.PreparedUpdate is not null,
                    IsVisible = managedTask => managedTask.Status == ManagedTaskStatus.Completed
                                              && handle?.PreparedUpdate is not null
                }
            ]
        }, async context =>
        {
            context.SetRunning($"正在下载：{asset.Name}");
            await ResumableDownloadAsync(downloadUrl, temporary, asset.Size,
                progress => Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                    var fraction = progress.TotalBytes > 0
                        ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                        : (double?)null;
                    var speed = DefaultDownloader.FormatSize(progress.Speed, true);
                    context.SetDescription($"下载速度：{speed}");
                    context.ReportProgress(fraction);
                }), context.CancellationToken);
            context.SetDescription("下载完成，正在校验");
            context.ReportProgress(1);
        });
        handle = new UpdateTaskHandle { Task = task };
        task.Start();
        await task.Completion;
        if (task.Status == ManagedTaskStatus.Cancelled)
            throw new OperationCanceledException("更新下载已取消。");
        if (task.Status == ManagedTaskStatus.Faulted)
            throw task.Exception ?? new IOException("更新包下载失败。");

        var actualSize = new FileInfo(temporary).Length;
        if (asset.Size <= 0 || actualSize != asset.Size)
        {
            File.Delete(temporary);
            throw new InvalidDataException($"更新包大小校验失败（预期 {asset.Size}，实际 {actualSize}）。");
        }
        if (asset.Sha256 is not null)
        {
            await using var package = File.OpenRead(temporary);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package));
            if (!actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporary);
                throw new InvalidDataException("更新包 SHA-256 校验失败。");
            }
        }
        File.Move(temporary, destination, true);
        return handle;
    }

    private const int DownloadBufferSize = 81920;
    private const double DownloadRetryBackoffSeconds = 1.5;

    // 更新包专用下载器：永不启用分片，支持断点续传。
    // - 每次失败保留已下载的临时文件，重试时通过 Range 从头偏移继续，而不是整包重下。
    // - 服务器不支持 Range（返回 200 而非 206）或已有文件损坏超出总长时，回退为整包重下。
    private static async Task ResumableDownloadAsync(string url, string path, long expectedSize,
        Action<ResourceDownloadProgressChangedEventArgs> progress, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(1, Data.ConfigEntry.DownloadMaxRetryCount);
        Exception? lastError = null;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                long resumeFrom = File.Exists(path) ? new FileInfo(path).Length : 0;
                await ResumableDownloadAttemptAsync(url, path, expectedSize, resumeFrom, progress, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                lastError = exception;
                // 429 是限流，重试不会立刻恢复，退避时间拉长
                if (exception.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(DownloadRetryBackoffSeconds * (attempt + 1) * 5),
                        cancellationToken);
                    continue;
                }
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(DownloadRetryBackoffSeconds * (attempt + 1)),
                        cancellationToken);
                    continue;
                }
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(DownloadRetryBackoffSeconds * (attempt + 1)),
                        cancellationToken);
                    continue;
                }
            }
            throw lastError ?? new IOException("更新包下载失败。");
        }
    }

    private static async Task ResumableDownloadAttemptAsync(string url, string path, long expectedSize, long initialOffset,
        Action<ResourceDownloadProgressChangedEventArgs> progress, CancellationToken cancellationToken)
    {
        long resumeFrom = initialOffset;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // 服务器与本地已有字节一致时，从断点请求剩余部分
        if (expectedSize > 0 && resumeFrom >= expectedSize)
            resumeFrom = 0; // 已有文件已完整或损坏，整包重下
        if (resumeFrom > 0)
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var response = await HttpUtil.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var supportsResume = response.StatusCode == HttpStatusCode.PartialContent
                             && response.Content.Headers.ContentRange?.From == resumeFrom;
        var total = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength
            ?? (expectedSize > 0 ? expectedSize : 0);

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        // 续传时追加写入；整包重下（含 Range 被忽略、文件已完整）时重新创建
        var fileMode = supportsResume ? FileMode.Append : FileMode.Create;
        await using var output = new FileStream(path, fileMode, FileAccess.Write, FileShare.ReadWrite, DownloadBufferSize,
            FileOptions.Asynchronous);

        var buffer = new byte[DownloadBufferSize];
        long downloaded = supportsResume ? resumeFrom : 0;
        var stopwatch = Stopwatch.StartNew();
        long lastReportBytes = downloaded;
        var lastReportTime = stopwatch.Elapsed;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            if (downloaded > total && total > 0)
                throw new IOException($"服务器返回数据超过预期长度（预期 {total}）。");

            var now = stopwatch.Elapsed;
            if (now - lastReportTime >= TimeSpan.FromMilliseconds(250))
            {
                ReportDownloadProgress(progress, downloaded, total, lastReportBytes, lastReportTime, now);
                lastReportBytes = downloaded;
                lastReportTime = now;
            }
        }
        await output.FlushAsync(cancellationToken);

        if (total > 0 && downloaded != total)
            throw new IOException($"下载不完整（预期 {total}，已下载 {downloaded}）。");

        progress(new ResourceDownloadProgressChangedEventArgs
        {
            Speed = 0,
            TotalBytes = total,
            EstimatedRemaining = TimeSpan.Zero,
            DownloadedBytes = downloaded,
            TotalCount = 1,
            CompletedCount = 1
        });
    }

    private static void ReportDownloadProgress(Action<ResourceDownloadProgressChangedEventArgs> progress,
        long downloaded, long total, long lastBytes, TimeSpan lastTime, TimeSpan now)
    {
        var deltaBytes = downloaded - lastBytes;
        var deltaTime = now - lastTime;
        var speed = deltaTime.TotalSeconds > 0 ? (long)(deltaBytes / deltaTime.TotalSeconds) : 0;
        var remainingSeconds = speed > 0 && total > downloaded ? (total - downloaded) / (double)speed : 0;
        progress(new ResourceDownloadProgressChangedEventArgs
        {
            Speed = speed,
            TotalBytes = total,
            EstimatedRemaining = TimeSpan.FromSeconds(remainingSeconds),
            DownloadedBytes = downloaded,
            TotalCount = 1,
            CompletedCount = 0
        });
    }

    private static void CompletePreparation(UpdateTaskHandle handle, PreparedUpdate update)
    {
        handle.PreparedUpdate = update;
        handle.Task.RefreshActions();
    }

    private static void CleanupOldUpdateDirectories(string currentDirectory)
    {
        try
        {
            var oldDirectories = Directory.GetDirectories(ConfigPath.UpdateFolderPath)
                .Select(path => new DirectoryInfo(path))
                .OrderByDescending(directory =>
                    directory.FullName.Equals(currentDirectory, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(directory => directory.LastWriteTimeUtc)
                .Skip(RetainedUpdateDirectories);
            foreach (var directory in oldDirectories)
            {
                try
                {
                    directory.Delete(true);
                }
                catch (Exception ex)
                {
                    Logger.Error($"删除过期更新目录失败：{directory.FullName}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("清理过期更新目录失败。", ex);
        }
    }

    private static ProcessStartInfo PrepareWindowsPortable(string zipPath, string updateDirectory, string target)
    {
        var extracted = Path.Combine(updateDirectory, "extracted");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        ZipFile.ExtractToDirectory(zipPath, extracted);
        var replacement = Directory.GetFiles(extracted, "*.exe", SearchOption.AllDirectories).SingleOrDefault()
                          ?? throw new InvalidDataException("portable 更新包中必须有且只有一个 EXE。");
        var script = Path.Combine(updateDirectory, "apply-update.ps1");
        // Windows PowerShell 5.1 把无 BOM 的脚本按 ANSI 解码，非 ASCII 路径会乱码，必须写入带 BOM 的 UTF-8
        File.WriteAllText(script, $$"""
            $ErrorActionPreference = 'Stop'
            $pidToWait = {{Environment.ProcessId}}
            $target = '{{Ps(target)}}'
            $source = '{{Ps(replacement)}}'
            $backup = $target + '.portal-update-old'
            $newFile = $target + '.portal-update-new'
            try {
              Wait-Process -Id $pidToWait -Timeout 60 -ErrorAction SilentlyContinue
              if (Get-Process -Id $pidToWait -ErrorAction SilentlyContinue) { throw 'Portal did not exit in time.' }
              Copy-Item -LiteralPath $source -Destination $newFile -Force
              if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
              Move-Item -LiteralPath $target -Destination $backup -Force
              Move-Item -LiteralPath $newFile -Destination $target -Force
              $process = Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target) -PassThru
              Start-Sleep -Seconds 5
              if ($process.HasExited) { throw 'The updated Portal exited immediately.' }
              Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            } catch {
              if (Test-Path -LiteralPath $backup) {
                Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
                Move-Item -LiteralPath $backup -Destination $target -Force
                Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target)
              }
              throw
            }
            """, new UTF8Encoding(true));
        return PowerShell(script, !CanWriteDirectory(Path.GetDirectoryName(target)!));
    }

    private static ProcessStartInfo PrepareWindowsInstaller(string zipPath, string updateDirectory)
    {
        var extracted = Path.Combine(updateDirectory, "installer");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        ZipFile.ExtractToDirectory(zipPath, extracted);
        var installer = Directory.GetFiles(extracted, "*.exe", SearchOption.AllDirectories).SingleOrDefault()
                        ?? throw new InvalidDataException("安装程序更新包中必须有且只有一个 EXE。");
        return new ProcessStartInfo(installer) { UseShellExecute = true };
    }

    private static ProcessStartInfo PrepareAppImage(string packagePath, string updateDirectory)
    {
        var target = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
            throw new InvalidOperationException("无法定位当前 AppImage；请从 AppImage 文件启动后重试。");
        var script = WriteUnixScript(updateDirectory, target, packagePath, false);
        return UnixScript(script, !CanWriteDirectory(Path.GetDirectoryName(target)!));
    }

    private static ProcessStartInfo PrepareLinuxPackage(
        string packagePath, string updateDirectory, string packageType, string processPath)
    {
        var workerScript = Path.Combine(updateDirectory, "install-package-update.sh");
        var launcherScript = Path.Combine(updateDirectory, "start-package-update.sh");
        var log = Path.Combine(updateDirectory, "install-package-update.log");
        var install = packageType switch
        {
            "deb" => $$"""
                if command -v apt-get >/dev/null 2>&1; then
                  apt-get install -y {{Sh(packagePath)}}
                else
                  dpkg -i {{Sh(packagePath)}}
                fi
                """,
            "rpm" => $$"""
                if command -v dnf >/dev/null 2>&1; then
                  dnf install -y {{Sh(packagePath)}}
                elif command -v zypper >/dev/null 2>&1; then
                  zypper --non-interactive install {{Sh(packagePath)}}
                else
                  rpm -Uvh {{Sh(packagePath)}}
                fi
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(packageType), packageType, "不支持的 Linux 安装包类型。")
        };
        File.WriteAllText(workerScript, $$"""
            #!/bin/sh
            set -eu
            log={{Sh(log)}}
            exec >>"$log" 2>&1
            echo "Portal package update started: $(date -Is)"
            pid='{{Environment.ProcessId}}'
            target={{Sh(processPath)}}
            uid="${PKEXEC_UID:-}"
            i=0
            while kill -0 "$pid" 2>/dev/null; do
              i=$((i + 1)); [ "$i" -gt 120 ] && exit 1
              sleep 0.5
            done
            {{install}}
            if [ -z "$uid" ]; then
              echo "pkexec did not provide the original user ID."
              exit 1
            fi
            passwd_entry="$(getent passwd "$uid")"
            user="$(printf '%s' "$passwd_entry" | cut -d: -f1)"
            home="$(printf '%s' "$passwd_entry" | cut -d: -f6)"
            if [ -z "$user" ]; then
              echo "Unable to resolve the original user ID: $uid"
              exit 1
            fi
            if [ -z "$home" ]; then
              echo "Unable to resolve the home directory for user: $user"
              exit 1
            fi
            display={{Sh(Environment.GetEnvironmentVariable("DISPLAY") ?? "")}}
            wayland_display={{Sh(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "")}}
            xauthority={{Sh(Environment.GetEnvironmentVariable("XAUTHORITY") ?? "")}}
            dbus_address={{Sh(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") ?? "")}}
            xdg_runtime_dir={{Sh(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "")}}
            [ -n "$xdg_runtime_dir" ] || xdg_runtime_dir="/run/user/$uid"
            echo "Starting the updated Portal process as $user."
            if ! command -v runuser >/dev/null 2>&1; then
              echo "The system does not provide runuser; Portal was installed but not restarted."
              exit 1
            fi
            runuser -u "$user" -- env \
              HOME="$home" \
              USER="$user" \
              LOGNAME="$user" \
              DISPLAY="$display" \
              WAYLAND_DISPLAY="$wayland_display" \
              XAUTHORITY="$xauthority" \
              DBUS_SESSION_BUS_ADDRESS="$dbus_address" \
              XDG_RUNTIME_DIR="$xdg_runtime_dir" \
              nohup "$target" >/dev/null 2>&1 &
            echo "Portal package update completed: $(date -Is)"
            """);
        File.WriteAllText(launcherScript, $$"""
            #!/bin/sh
            set -eu
            worker={{Sh(workerScript)}}
            log={{Sh(log)}}
            echo "Portal update authentication accepted: $(date -Is)" >>"$log"
            nohup "$worker" >>"$log" 2>&1 </dev/null &
            exit 0
            """);
        RunAndWait("/bin/chmod", "+x", workerScript, launcherScript);
        return UnixScript(launcherScript, true);
    }

    private static ProcessStartInfo PrepareMacApp(string packagePath, string updateDirectory, string processPath)
    {
        var marker = $"{Path.DirectorySeparatorChar}Contents{Path.DirectorySeparatorChar}MacOS{Path.DirectorySeparatorChar}";
        var markerIndex = processPath.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) throw new InvalidOperationException("当前程序不在 macOS .app 应用程序包中。");
        var target = processPath[..markerIndex];
        if (target.StartsWith("/Volumes/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("程序正在 DMG 中运行。请先将 Portal.app 拖到“应用程序”文件夹。");

        var extracted = Path.Combine(updateDirectory, "mac-app");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        Directory.CreateDirectory(extracted);
        RunAndWait("/usr/bin/ditto", "-x", "-k", packagePath, extracted);
        var replacement = Path.Combine(extracted, "Portal.app");
        if (!File.Exists(Path.Combine(replacement, "Contents", "MacOS", "Portal.Desktop")))
            throw new InvalidDataException("macOS 更新包结构无效。");
        var script = WriteUnixScript(updateDirectory, target, replacement, true);
        return UnixScript(script, !CanWriteDirectory(Path.GetDirectoryName(target)!));
    }

    private static string WriteUnixScript(string directory, string target, string source, bool isMac)
    {
        var script = Path.Combine(directory, "apply-update.sh");
        var launch = isMac ? $"/usr/bin/open -n {Sh(target)}" : $"{Sh(target)} >/dev/null 2>&1 &";
        File.WriteAllText(script, $$"""
            #!/bin/sh
            set -eu
            pid='{{Environment.ProcessId}}'
            target={{Sh(target)}}
            source={{Sh(source)}}
            backup="${target}.portal-update-old"
            cleanup_new="${target}.portal-update-new"
            i=0
            while kill -0 "$pid" 2>/dev/null; do
              i=$((i + 1)); [ "$i" -gt 120 ] && exit 1
              sleep 0.5
            done
            rm -rf "$cleanup_new" "$backup"
            cp -R "$source" "$cleanup_new"
            {{(isMac ? ":" : "chmod --reference=\"$target\" \"$cleanup_new\" 2>/dev/null || chmod +x \"$cleanup_new\"")}}
            mv "$target" "$backup"
            if ! mv "$cleanup_new" "$target"; then mv "$backup" "$target"; exit 1; fi
            if ! {{launch}}; then rm -rf "$target"; mv "$backup" "$target"; {{launch}}; exit 1; fi
            sleep 5
            rm -rf "$backup"
            """);
        RunAndWait("/bin/chmod", "+x", script);
        return script;
    }

    private static ProcessStartInfo PowerShell(string script, bool elevate)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = elevate,
            CreateNoWindow = !elevate,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-WindowStyle");
        info.ArgumentList.Add("Hidden");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        if (elevate) info.Verb = "runas";
        return info;
    }

    private static ProcessStartInfo UnixScript(string script, bool elevate)
    {
        if (!elevate) return new ProcessStartInfo(script) { UseShellExecute = false };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!File.Exists("/usr/bin/pkexec")) throw new UnauthorizedAccessException("目标位置不可写，且系统未安装 pkexec。");
            var info = new ProcessStartInfo("/usr/bin/pkexec") { UseShellExecute = false };
            info.ArgumentList.Add(script);
            return info;
        }

        var command = $"do shell script {AppleScript(Sh(script))} with administrator privileges";
        var osascript = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
        osascript.ArgumentList.Add("-e");
        osascript.ArgumentList.Add(command);
        return osascript;
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".portal-write-test-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception exception)
        {
            Logger.Warning($"检测目录写入权限失败：{directory}{Environment.NewLine}{exception}");
            return false;
        }
    }

    private static void RunAndWait(string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"无法启动 {fileName}。");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{fileName} 执行失败（{process.ExitCode}）。");
    }

    private static string Ps(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string Sh(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    private static string AppleScript(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
