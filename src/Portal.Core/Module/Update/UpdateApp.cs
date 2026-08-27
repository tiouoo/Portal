using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MinecraftLaunch.Base.EventArgs;
using MinecraftLaunch.Components.Downloader;
using MinecraftLaunch.Utilities;
using Portal.Core.Const;
using Portal.Core.Module.Initialize;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Modules.Events;
using Tio.Avalonia.Standard.Modules.Tasks;
using Tio.Avalonia.Standard.Tab.Gateway;

namespace Portal.Core.Module.Update;

public static class UpdateApp
{
    private const int RetainedUpdateDirectories = 4;

    private const int DownloadBufferSize = 81920;
    private const double DownloadRetryBackoffSeconds = 1.5;
    private static readonly Lock PreparationLock = new();
    private static Task<PreparedUpdate?>? _automaticPreparationTask;
    private static Task<PreparedUpdate?>? _manualPreparationTask;

    public static PreparedUpdate? ReadyUpdate { get; private set; }

    public static Task<PreparedUpdate?> Prepare(TopLevel? sender, bool silent = false)
    {
        lock (PreparationLock)
        {
            if (ReadyUpdate is not null)
            {
                if (!silent) Data.UiProperty.IsManualUpdateRequested = true;
                return Task.FromResult<PreparedUpdate?>(ReadyUpdate);
            }

            if (silent)
            {
                if (_automaticPreparationTask is { IsCompleted: false }) return _automaticPreparationTask;
                _automaticPreparationTask = PrepareCore(null, true);
                return _automaticPreparationTask;
            }

            Data.UiProperty.IsManualUpdateRequested = true;
            if (_manualPreparationTask is { IsCompleted: false }) return _manualPreparationTask;
            _manualPreparationTask = PrepareCore(sender, false);
            return _manualPreparationTask;
        }
    }

    private static async Task<PreparedUpdate?> PrepareCore(TopLevel? sender, bool silent)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.Info(LogLanguageManager.Instance.update_prepareStart.CurrentValue());
        try
        {
            while (true)
            {
                var release = await UpdateChecker.GetRelease();
                if (!UpdateChecker.IsNewer(release))
                {
                    if (!silent)
                    {
                        Data.UiProperty.IsManualUpdateRequested = false;
                        Data.UiProperty.FoundNewVersion = false;
                        Data.UiProperty.IsLatestVersion = true;
                    }
                    if (!silent && sender is not null)
                        sender.Notice(CommonLanguageManager.Instance.update_alreadyLatest.CurrentValue(), NotificationType.Success);
                    return null;
                }

                if (!silent)
                {
                    Data.UiProperty.IsLatestVersion = false;
                    Data.UiProperty.NewVersion = release.Title;
                    Data.UiProperty.FoundNewVersion = true;
                }
                var packageType = Data.Instance.PackageType.Trim().ToLowerInvariant();
                var asset = await UpdateChecker.ResolveDownloadMetadata(SelectAsset(release, packageType));
                var releaseIdentity = GetReleaseIdentity(release, asset);
                var updateDirectory = Path.Combine(ConfigPath.UpdateFolderPath,
                    $"{releaseIdentity[..16]}-{(silent ? "auto" : "manual")}");
                Directory.CreateDirectory(updateDirectory);
                CleanupOldUpdateDirectories(updateDirectory);
                var packagePath = Path.Combine(updateDirectory, asset.Name);
                Logger.Info(string.Format(LogLanguageManager.Instance.update_packageSelected.CurrentValue(), asset.Name, updateDirectory));

                if (!silent && sender is not null)
                    sender.Notice(string.Format(CommonLanguageManager.Instance.update_downloading.CurrentValue(), asset.Name));
                if (silent)
                {
                    Data.UiProperty.IsAutomaticUpdateDownloading = true;
                    Data.UiProperty.AutomaticUpdateDownloadPercent = 0;
                }
                else
                {
                    Data.UiProperty.IsUpdateDownloading = true;
                    Data.UiProperty.UpdateDownloadPercent = 0;
                }
                var taskHandle = await Download(asset, packagePath, !silent);
                if (silent)
                    Data.UiProperty.IsAutomaticUpdateDownloading = false;
                else
                    Data.UiProperty.IsUpdateDownloading = false;

                var latestRelease = await UpdateChecker.GetRelease();
                var latestAsset = await UpdateChecker.ResolveDownloadMetadata(SelectAsset(latestRelease, packageType));
                if (!releaseIdentity.Equals(GetReleaseIdentity(latestRelease, latestAsset), StringComparison.Ordinal))
                {
                    Directory.Delete(updateDirectory, true);
                    if (!silent && sender is not null)
                        sender.Notice(CommonLanguageManager.Instance.update_newReleaseDownloading.CurrentValue());
                    continue;
                }

                PreparedUpdate preparedUpdate;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && packageType == "installer")
                {
                    preparedUpdate = new PreparedUpdate(
                        await Task.Run(() => PrepareWindowsInstaller(packagePath, updateDirectory)), true,
                        ReleaseIdentity: releaseIdentity);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && packageType is "deb" or "rpm")
                {
                    var processPath = Environment.ProcessPath
                                      ?? throw new InvalidOperationException(CommonLanguageManager.Instance.update_cannotDetermineCurrentPath.CurrentValue());
                    preparedUpdate = new PreparedUpdate(
                        await Task.Run(() => PrepareLinuxPackage(packagePath, updateDirectory, packageType, processPath)),
                        true, true, releaseIdentity);
                }
                else
                {
                    var path = Environment.ProcessPath
                               ?? throw new InvalidOperationException(CommonLanguageManager.Instance.update_cannotDetermineCurrentPath.CurrentValue());
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && packageType == "appimage")
                    {
                        var appImageUpdate = await Task.Run(() => PrepareAppImage(packagePath, updateDirectory));
                        preparedUpdate = appImageUpdate with
                        {
                            ReleaseIdentity = releaseIdentity
                        };
                    }
                    else
                    {
                        ProcessStartInfo updater;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && packageType == "portable")
                            updater = await Task.Run(() => PrepareWindowsPortable(packagePath, updateDirectory, path));
                        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && packageType is "app" or "dmg")
                            updater = await Task.Run(() => PrepareMacApp(packagePath, updateDirectory, path));
                        else
                            throw new NotSupportedException(string.Format(CommonLanguageManager.Instance.update_unsupportedInstallType.CurrentValue(), packageType));
                        preparedUpdate = new PreparedUpdate(updater, false, ReleaseIdentity: releaseIdentity);
                    }
                }

                CompletePreparation(taskHandle, preparedUpdate);
                lock (PreparationLock)
                {
                    if (ReadyUpdate is null)
                    {
                        ReadyUpdate = preparedUpdate;
                        Data.UiProperty.IsUpdateReady = true;
                    }
                }
                Logger.Info(string.Format(LogLanguageManager.Instance.update_prepareComplete.CurrentValue(), stopwatch.ElapsedMilliseconds));
                return ReadyUpdate;
            }
        }
        catch (Exception ex)
        {
            if (silent)
                Data.UiProperty.IsAutomaticUpdateDownloading = false;
            else
                Data.UiProperty.IsUpdateDownloading = false;
            Logger.Error(LogLanguageManager.Instance.update_prepareFailed.CurrentValue(), ex);
            if (!silent && sender is not null)
                sender.Notice(string.Format(CommonLanguageManager.Instance.update_failed.CurrentValue(), ex.Message), NotificationType.Error);
            return null;
        }
    }

    public static async Task Apply(PreparedUpdate update)
    {
        var release = await UpdateChecker.GetRelease();
        var packageType = Data.Instance.PackageType.Trim().ToLowerInvariant();
        var asset = await UpdateChecker.ResolveDownloadMetadata(SelectAsset(release, packageType));
        if (!GetReleaseIdentity(release, asset).Equals(update.ReleaseIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException(CommonLanguageManager.Instance.update_downloadedReleaseChanged.CurrentValue());
        if (!await ApplicationEvents.RaiseAppExiting()) return;
        ConfigSaver.FlushConfig();
        using var process = Process.Start(update.StartInfo)
                            ?? throw new InvalidOperationException(CommonLanguageManager.Instance.update_cannotStartInstaller.CurrentValue());
        if (update.WaitForStart)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.update_installerFailedToStart.CurrentValue(), process.ExitCode));
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
            _ => throw new PlatformNotSupportedException(string.Format(CommonLanguageManager.Instance.update_unsupportedArchitecture.CurrentValue(), RuntimeInformation.ProcessArchitecture))
        };
        string expectedName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (arch != "x64") throw new PlatformNotSupportedException(CommonLanguageManager.Instance.update_noWindowsArmPackage.CurrentValue());
            expectedName = packageType switch
            {
                "installer" => "Portal.win.x64.installer.zip",
                "portable" => "Portal.win.x64.portable.zip",
                _ => throw new NotSupportedException(string.Format(CommonLanguageManager.Instance.update_unsupportedWindowsInstallType.CurrentValue(), packageType))
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (arch != "x64") throw new PlatformNotSupportedException(CommonLanguageManager.Instance.update_linuxOnlyX64.CurrentValue());
            expectedName = packageType switch
            {
                "appimage" => "Portal.linux.x64.AppImage",
                "deb" => "Portal.linux.x64.deb",
                "rpm" => "Portal.linux.x64.rpm",
                _ => throw new NotSupportedException(string.Format(CommonLanguageManager.Instance.update_unsupportedLinuxInstallType.CurrentValue(), packageType))
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (packageType is not ("app" or "dmg")) throw new NotSupportedException(CommonLanguageManager.Instance.update_macOnlyAppBundle.CurrentValue());
            if (arch == "arm") throw new PlatformNotSupportedException(CommonLanguageManager.Instance.update_noArmMac.CurrentValue());
            expectedName = $"Portal.osx.mac.{arch}.app.zip";
        }
        else
        {
            throw new PlatformNotSupportedException(CommonLanguageManager.Instance.update_osNotSupported.CurrentValue());
        }

        return release.Assets.SingleOrDefault(asset => asset.Name.Equals(expectedName, StringComparison.Ordinal))
               ?? throw new FileNotFoundException(string.Format(CommonLanguageManager.Instance.update_packageNotFound.CurrentValue(), expectedName));
    }

    private static async Task<UpdateTaskHandle?> Download(UpdateAsset asset, string destination, bool showTask)
    {
        if (await IsValidPackage(destination, asset))
        {
            if (showTask)
                Data.UiProperty.UpdateDownloadPercent = 100;
            else
                Data.UiProperty.AutomaticUpdateDownloadPercent = 100;
            return null;
        }

        var downloadUrl = GithubMirror.Apply(asset.DownloadUrl);
        if (!downloadUrl.Equals(asset.DownloadUrl, StringComparison.Ordinal))
            Logger.Info($"Downloading update via GitHub mirror: {downloadUrl}");
        var temporary = destination + ".download";
        if (!showTask)
        {
            await ResumableDownloadAsync(downloadUrl, temporary, asset.Size, progress =>
            {
                if (progress.TotalBytes <= 0) return;
                var fraction = Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1);
                Dispatcher.UIThread.Post(() =>
                    Data.UiProperty.AutomaticUpdateDownloadPercent = (int)Math.Round(fraction * 100));
            }, CancellationToken.None);
            await VerifyAndMovePackage(asset, temporary, destination);
            return null;
        }

        UpdateTaskHandle? handle = null;
        var task = TaskManager.Instance.CreateTask(new TaskOptions
        {
            Name = CommonLanguageManager.Instance.update_taskName.CurrentValue(),
            Description = string.Format(CommonLanguageManager.Instance.update_taskConnecting.CurrentValue(), asset.Name),
            Progress = 0,
            Actions =
            [
                new TaskActionDefinition
                {
                    Name = CommonLanguageManager.Instance.update_cancelDownload.CurrentValue(),
                    Description = CommonLanguageManager.Instance.update_cancelDownloadDescription.CurrentValue(),
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
        }, async context =>
        {
            context.SetRunning(string.Format(CommonLanguageManager.Instance.update_downloadingName.CurrentValue(), asset.Name));
            await ResumableDownloadAsync(downloadUrl, temporary, asset.Size,
                progress => Dispatcher.UIThread.Post(() =>
                {
                    if (context.Task.IsTerminal || context.Task.IsCancellationRequested) return;
                     var fraction = progress.TotalBytes > 0
                        ? Math.Clamp((double)progress.DownloadedBytes / progress.TotalBytes, 0, 1)
                        : (double?)null;
                    var speed = DefaultDownloader.FormatSize(progress.Speed, true);
                     context.SetDescription(string.Format(CommonLanguageManager.Instance.update_downloadSpeed.CurrentValue(), speed));
                     context.ReportProgress(fraction);
                     Data.UiProperty.UpdateDownloadPercent = fraction is null ? 0 : (int)Math.Round(fraction.Value * 100);
                }), context.CancellationToken);
            context.SetDescription(CommonLanguageManager.Instance.update_downloadCompleteVerifying.CurrentValue());
            context.ReportProgress(1);
        });
        handle = new UpdateTaskHandle { Task = task };
        task.Start();
        await task.Completion;
        if (task.Status == ManagedTaskStatus.Cancelled)
            throw new OperationCanceledException(CommonLanguageManager.Instance.update_downloadCancelled.CurrentValue());
        if (task.Status == ManagedTaskStatus.Faulted)
            throw task.Exception ?? new IOException(CommonLanguageManager.Instance.update_downloadFailed.CurrentValue());

        await VerifyAndMovePackage(asset, temporary, destination);
        return handle;
    }

    private static async Task VerifyAndMovePackage(UpdateAsset asset, string temporary, string destination)
    {
        var actualSize = new FileInfo(temporary).Length;
        if (asset.Size <= 0 || actualSize != asset.Size)
        {
            File.Delete(temporary);
            throw new InvalidDataException(string.Format(CommonLanguageManager.Instance.update_sizeVerificationFailed.CurrentValue(), asset.Size, actualSize));
        }

        if (asset.Sha256 is not null)
        {
            await using var package = File.OpenRead(temporary);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package));
            if (!actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporary);
                throw new InvalidDataException(CommonLanguageManager.Instance.update_sha256VerificationFailed.CurrentValue());
            }
        }

        File.Move(temporary, destination, true);
    }

    private static async Task<bool> IsValidPackage(string path, UpdateAsset asset)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != asset.Size) return false;
        if (asset.Sha256 is null) return true;
        await using var package = File.OpenRead(path);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(package));
        return actualHash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);
    }


    private static async Task ResumableDownloadAsync(string url, string path, long expectedSize,
        Action<ResourceDownloadProgressChangedEventArgs> progress, CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(1, Data.ConfigEntry.DownloadMaxRetryCount);
        Exception? lastError = null;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var resumeFrom = File.Exists(path) ? new FileInfo(path).Length : 0;
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

            throw lastError ?? new IOException(CommonLanguageManager.Instance.update_downloadFailed.CurrentValue());
        }
    }

    private static async Task ResumableDownloadAttemptAsync(string url, string path, long expectedSize,
        long initialOffset,
        Action<ResourceDownloadProgressChangedEventArgs> progress, CancellationToken cancellationToken)
    {
        var resumeFrom = initialOffset;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (expectedSize > 0 && resumeFrom >= expectedSize)
            resumeFrom = 0;
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

        var fileMode = supportsResume ? FileMode.Append : FileMode.Create;
        await using var output = new FileStream(path, fileMode, FileAccess.Write, FileShare.ReadWrite,
            DownloadBufferSize,
            FileOptions.Asynchronous);

        var buffer = new byte[DownloadBufferSize];
        var downloaded = supportsResume ? resumeFrom : 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReportBytes = downloaded;
        var lastReportTime = stopwatch.Elapsed;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;
            if (downloaded > total && total > 0)
                throw new IOException(string.Format(CommonLanguageManager.Instance.update_serverDataTooLong.CurrentValue(), total));

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
            throw new IOException(string.Format(CommonLanguageManager.Instance.update_incompleteDownload.CurrentValue(), total, downloaded));

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

    private static void CompletePreparation(UpdateTaskHandle? handle, PreparedUpdate update)
    {
        if (handle is null) return;
        handle.PreparedUpdate = update;
        handle.Task.RefreshActions();
    }

    private static string GetReleaseIdentity(UpdateRelease release, UpdateAsset asset)
    {
        var identity = $"{release.Title}\n{release.Sequence}\n{asset.Name}\n{asset.DownloadUrl}\n{asset.Size}\n{asset.Sha256}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
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
                try
                {
                    directory.Delete(true);
                }
                catch (Exception ex)
                {
                    Logger.Error(string.Format(LogLanguageManager.Instance.update_cleanupOldDirectoryFailed.CurrentValue(), directory.FullName), ex);
                }
        }
        catch (Exception ex)
        {
            Logger.Error(LogLanguageManager.Instance.update_cleanupOldDirectoriesFailed.CurrentValue(), ex);
        }
    }

    private static ProcessStartInfo PrepareWindowsPortable(string zipPath, string updateDirectory, string target)
    {
        var extracted = Path.Combine(updateDirectory, "extracted");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        ZipFile.ExtractToDirectory(zipPath, extracted);
        var replacement = Directory.GetFiles(extracted, "*.exe", SearchOption.AllDirectories).SingleOrDefault()
                          ?? throw new InvalidDataException(CommonLanguageManager.Instance.update_portableMustHaveOneExe.CurrentValue());
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
                                    """, new UTF8Encoding(true));
        return PowerShell(script, !CanWriteDirectory(Path.GetDirectoryName(target)!));
    }

    private static ProcessStartInfo PrepareWindowsInstaller(string zipPath, string updateDirectory)
    {
        var extracted = Path.Combine(updateDirectory, "installer");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        ZipFile.ExtractToDirectory(zipPath, extracted);
        var installer = Directory.GetFiles(extracted, "*.exe", SearchOption.AllDirectories).SingleOrDefault()
                        ?? throw new InvalidDataException(CommonLanguageManager.Instance.update_installerMustHaveOneExe.CurrentValue());
        var startInfo = new ProcessStartInfo(installer) { UseShellExecute = true };
        startInfo.ArgumentList.Add("/VERYSILENT");
        startInfo.ArgumentList.Add("/SUPPRESSMSGBOXES");
        startInfo.ArgumentList.Add("/NORESTART");
        startInfo.ArgumentList.Add("/SP-");
        return startInfo;
    }

    private static PreparedUpdate PrepareAppImage(string packagePath, string updateDirectory)
    {
        var target = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
            throw new InvalidOperationException(CommonLanguageManager.Instance.update_appImageNotLocated.CurrentValue());
        var workerScript = Path.Combine(updateDirectory, "apply-appimage-update.sh");
        var launcherScript = Path.Combine(updateDirectory, "start-appimage-update.sh");
        var log = Path.Combine(updateDirectory, "apply-appimage-update.log");
        File.WriteAllText(workerScript, $$"""
                                          #!/bin/sh
                                          set -eu
                                          log={{Sh(log)}}
                                          exec >>"$log" 2>&1
                                          echo "Portal AppImage update started: $(date -Is)"
                                          pid='{{Environment.ProcessId}}'
                                          target={{Sh(target)}}
                                          source={{Sh(packagePath)}}
                                          backup="${target}.portal-update-old"
                                          cleanup_new="${target}.portal-update-new"
                                          i=0
                                          while kill -0 "$pid" 2>/dev/null; do
                                            i=$((i + 1)); [ "$i" -gt 120 ] && exit 1
                                            sleep 0.5
                                          done
                                          rm -rf "$cleanup_new" "$backup"
                                          cp -R "$source" "$cleanup_new"
                                          chmod --reference="$target" "$cleanup_new" 2>/dev/null || chmod +x "$cleanup_new"
                                          if ! mv "$target" "$backup"; then
                                            rm -rf "$cleanup_new"
                                            echo "Failed to move the current AppImage aside; update aborted."
                                            exit 1
                                          fi
                                          if ! mv "$cleanup_new" "$target"; then
                                            mv "$backup" "$target"
                                            echo "Failed to install the updated AppImage; rolled back."
                                            exit 1
                                          fi
                                          if [ -n "${PKEXEC_UID:-}" ]; then
                                            passwd_entry="$(getent passwd "$PKEXEC_UID")"
                                            user="$(printf '%s' "$passwd_entry" | cut -d: -f1)"
                                            home="$(printf '%s' "$passwd_entry" | cut -d: -f6)"
                                            if [ -z "$user" ]; then
                                              echo "Unable to resolve the original user ID: $PKEXEC_UID"
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
                                            [ -n "$xdg_runtime_dir" ] || xdg_runtime_dir="/run/user/$PKEXEC_UID"
                                            if ! command -v runuser >/dev/null 2>&1; then
                                              echo "The system does not provide runuser; Portal was replaced but not restarted."
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
                                          else
                                            nohup "$target" >/dev/null 2>&1 &
                                          fi
                                          sleep 5
                                          rm -rf "$backup"
                                          echo "Portal AppImage update completed: $(date -Is)"
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
        return new PreparedUpdate(
            UnixScript(launcherScript, !CanWriteDirectory(Path.GetDirectoryName(target)!)), false, true);
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
            _ => throw new ArgumentOutOfRangeException(nameof(packageType), packageType, CommonLanguageManager.Instance.update_unsupportedLinuxPackageType.CurrentValue())
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
        var marker =
            $"{Path.DirectorySeparatorChar}Contents{Path.DirectorySeparatorChar}MacOS{Path.DirectorySeparatorChar}";
        var markerIndex = processPath.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) throw new InvalidOperationException(CommonLanguageManager.Instance.update_notInMacAppBundle.CurrentValue());
        var target = processPath[..markerIndex];
        if (target.StartsWith("/Volumes/", StringComparison.Ordinal))
            throw new UnauthorizedAccessException(CommonLanguageManager.Instance.update_runningInDmg.CurrentValue());

        var extracted = Path.Combine(updateDirectory, "mac-app");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, true);
        Directory.CreateDirectory(extracted);
        RunAndWait("/usr/bin/ditto", "-x", "-k", packagePath, extracted);
        var replacement = Path.Combine(extracted, "Portal.app");
        if (!File.Exists(Path.Combine(replacement, "Contents", "MacOS", "Portal.Desktop")))
            throw new InvalidDataException(CommonLanguageManager.Instance.update_macPackageInvalid.CurrentValue());
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
            if (!File.Exists("/usr/bin/pkexec")) throw new UnauthorizedAccessException(CommonLanguageManager.Instance.update_pkexecMissing.CurrentValue());
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
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception exception)
        {
            Logger.Warning(string.Format(LogLanguageManager.Instance.update_writePermissionCheckFailed.CurrentValue(), directory, Environment.NewLine + exception));
            return false;
        }
    }

    private static void RunAndWait(string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.common_cannotStart.CurrentValue(), fileName));
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(string.Format(CommonLanguageManager.Instance.update_runFailed.CurrentValue(), fileName, process.ExitCode));
    }

    private static string Ps(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string Sh(string value)
    {
        return "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    }

    private static string AppleScript(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    public sealed record PreparedUpdate(ProcessStartInfo StartInfo, bool RunsInstaller, bool WaitForStart = false,
        string ReleaseIdentity = "");

    private sealed class UpdateTaskHandle
    {
        public required ManagedTask Task { get; init; }
        public PreparedUpdate? PreparedUpdate { get; set; }
    }
}
