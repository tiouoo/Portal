using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MinecraftLaunch.Base.Enums;
using MinecraftLaunch.Base.Models.Network;
using MinecraftLaunch.Components.Downloader;
using Portal.Const;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Module.Update;

public static class UpdateApp
{
    public sealed record PreparedUpdate(ProcessStartInfo StartInfo, bool RunsInstaller);

    private sealed class UpdateTaskHandle
    {
        public required ManagedTask Task { get; init; }
        public PreparedUpdate? PreparedUpdate { get; set; }
    }

    public static async Task<PreparedUpdate?> Prepare(TopLevel sender)
    {
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
            var updateDirectory = Path.Combine(ConfigPath.UpdateFolderPath, release.Sequence > 0
                ? release.Sequence.ToString()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            Directory.CreateDirectory(updateDirectory);
            var packagePath = Path.Combine(updateDirectory, asset.Name);

            sender.Notice($"正在下载 {asset.Name}", NotificationType.Information);
            var taskHandle = await Download(asset, packagePath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && packageType == "installer")
            {
                var installerUpdate = new PreparedUpdate(PrepareWindowsInstaller(packagePath, updateDirectory), true);
                CompletePreparation(taskHandle, installerUpdate);
                return installerUpdate;
            }

            var processPath = Environment.ProcessPath
                              ?? throw new InvalidOperationException("无法确定当前程序路径。");
            ProcessStartInfo updater;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && packageType == "portable")
                updater = PrepareWindowsPortable(packagePath, updateDirectory, processPath);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && packageType == "appimage")
                updater = PrepareAppImage(packagePath, updateDirectory);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && packageType is "app" or "dmg")
                updater = PrepareMacApp(packagePath, updateDirectory, processPath);
            else
                throw new NotSupportedException($"当前系统不支持安装类型“{packageType}”的自动更新。");

            var preparedUpdate = new PreparedUpdate(updater, false);
            CompletePreparation(taskHandle, preparedUpdate);
            return preparedUpdate;
        }
        catch (Exception ex)
        {
            sender.Notice($"更新失败：{ex.Message}", NotificationType.Error);
            return null;
        }
    }

    public static async Task Apply(PreparedUpdate update)
    {
        if (!await ApplicationEvents.RaiseAppExiting()) return;
        Process.Start(update.StartInfo);
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
            if (packageType != "appimage") throw new NotSupportedException("Linux 自动更新目前仅支持 AppImage。");
            expectedName = $"Portal.linux.{arch}.AppImage";
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
        var temporary = destination + ".download";
        if (File.Exists(temporary)) File.Delete(temporary);
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
            var request = new DownloadRequest(asset.DownloadUrl, temporary, asset.Size)
            {
                ProgressChanged = progress => Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                    var fraction = progress.TotalBytes > 0
                        ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                        : (double?)null;
                    var speed = DefaultDownloader.FormatSize(progress.Speed, true);
                    context.SetDescription($"下载速度：{speed}");
                    context.ReportProgress(fraction);
                })
            };

            var result = await new DefaultDownloader().DownloadAsync(request, context.CancellationToken);
            switch (result.Type)
            {
                case DownloadResultType.Successful:
                    context.SetDescription("下载完成，正在校验");
                    context.ReportProgress(1);
                    break;
                case DownloadResultType.Cancelled:
                    context.Task.RequestCancellation();
                    throw new OperationCanceledException(context.CancellationToken);
                case DownloadResultType.Failed:
                    throw result.Exception ?? new IOException("更新包下载失败。");
                default:
                    throw new InvalidOperationException($"未知下载结果：{result.Type}");
            }
        });
        handle = new UpdateTaskHandle { Task = task };
        task.Start();
        await task.Completion;
        if (task.Status == ManagedTaskStatus.Cancelled)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw new OperationCanceledException("更新下载已取消。");
        }
        if (task.Status == ManagedTaskStatus.Faulted)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw task.Exception ?? new IOException("更新包下载失败。");
        }

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

    private static void CompletePreparation(UpdateTaskHandle handle, PreparedUpdate update)
    {
        handle.PreparedUpdate = update;
        handle.Task.RefreshActions();
    }

    private static ProcessStartInfo PrepareWindowsPortable(string zipPath, string updateDirectory, string target)
    {
        var extracted = Path.Combine(updateDirectory, "extracted");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        ZipFile.ExtractToDirectory(zipPath, extracted);
        var replacement = Directory.GetFiles(extracted, "*.exe", SearchOption.AllDirectories).SingleOrDefault()
                          ?? throw new InvalidDataException("portable 更新包中必须有且只有一个 EXE。");
        var script = Path.Combine(updateDirectory, "apply-update.ps1");
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
            """);
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
        catch { return false; }
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
