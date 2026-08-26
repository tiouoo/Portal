using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Iridium.Models.Authentication;
using Iridium.Java;
using Iridium.Models.Minecraft;
using Iridium.Models.Java;
using Portal.Core.Minecraft.Classes;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft;

public static class LaunchCustomization
{
    public static Dictionary<string, string> BuildPlaceholders(MinecraftInstance instance, Account? account,
        JavaEntry? java, MinecraftLaunchOptions options)
    {
        var entry = instance.MinecraftEntry;
        var independent = instance.RequiresIndependentInstance ||
                          instance.JavaConfig?.EnableIndependentInstance == true;
        var gameDirectory = entry != null
            ? IridiumEntryHelper.GetWorkingPath(instance.Context!, independent)
            : instance.InstanceFolderPath;
        var loaders = entry != null
            ? string.Join(" ", entry.Loaders.Select(loader => $"{loader.Type} {loader.Version}".Trim()))
            : string.Empty;

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{instance_name}"] = instance.InstanceName,
            ["{instance_id}"] = entry?.Id ?? instance.BedrockConfig?.Name ?? string.Empty,
            ["{instance_path}"] = instance.MinecraftPath ?? string.Empty,
            ["{game_dir}"] = gameDirectory ?? string.Empty,
            ["{minecraft_folder}"] = entry != null ? IridiumEntryHelper.GetMinecraftRoot(instance.Context!) : instance.FolderPath ?? string.Empty,
            ["{natives_dir}"] = entry != null ? IridiumEntryHelper.GetNativesDirectory(instance.Context!) : string.Empty,
            ["{assets_dir}"] = entry != null ? IridiumEntryHelper.GetAssetsDirectory(instance.Context!) : string.Empty,
            ["{version}"] = entry?.MinecraftVersion ?? instance.BedrockConfig?.Version ?? string.Empty,
            ["{version_type}"] = entry?.Type.ToString() ?? string.Empty,
            ["{loader}"] = loaders,
            ["{edition}"] = instance.Type == MinecraftInstanceType.Bedrock ? "bedrock" : "java",
            ["{player_name}"] = account?.Name ?? options.Account?.Name ?? string.Empty,
            ["{player_uuid}"] = account?.Uuid.ToString() ?? options.Account?.Uuid?.ToString() ?? string.Empty,
            ["{account_type}"] = account?.Type.ToString() ?? options.Account?.AccountType.ToString() ?? string.Empty,
            ["{access_token}"] = account?.AccessToken ?? string.Empty,
            ["{java_path}"] = java?.JavaPath ?? string.Empty,
            ["{java_dir}"] = Path.GetDirectoryName(java?.JavaPath) ?? string.Empty,
            ["{java_version}"] = java?.Version ?? string.Empty,
            ["{java_major}"] = java?.MajorVersion.ToString() ?? string.Empty,
            ["{width}"] = options.WindowWidth.ToString(),
            ["{height}"] = options.WindowHeight.ToString(),
            ["{max_memory}"] = options.MaxMemory.ToString(),
            ["{launcher_dir}"] = AppContext.BaseDirectory,
            ["{launcher_path}"] = Environment.ProcessPath ?? string.Empty
        };

        placeholders["{title}"] = Apply(options.WindowTitle, placeholders) ?? string.Empty;
        return placeholders;
    }

    public static string? Apply(string? template, IReadOnlyDictionary<string, string> placeholders)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var builder = new StringBuilder(template);
        foreach (var (key, value) in placeholders)
            builder.Replace(key, value);
        return builder.ToString();
    }

    public static IReadOnlyList<string> SplitArguments(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return [];

        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var character in commandLine)
            switch (character)
            {
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ':
                case '\r':
                case '\n':
                case '\t':
                    if (!inQuotes)
                    {
                        if (current.Length > 0)
                        {
                            arguments.Add(current.ToString());
                            current.Clear();
                        }

                        break;
                    }

                    current.Append(character);
                    break;
                default:
                    current.Append(character);
                    break;
            }

        if (current.Length > 0)
            arguments.Add(current.ToString());
        return arguments;
    }

    public static async Task<int> RunShellCommandAsync(string command, string? workingDirectory,
        CancellationToken cancellationToken)
    {
        Logger.Info(string.Format(LogLanguageManager.Instance.launchCustomization_runCommand.CurrentValue(),
            workingDirectory ?? CommonLanguageManager.Instance.launch_defaultDirectory.CurrentValue()));
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { Arguments = $"/c {command}" }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        Logger.Debug(string.Format(LogLanguageManager.Instance.launchCustomization_commandProcessStarted.CurrentValue(), process.Id));
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (Exception exception)
            {
                Logger.Debug(string.Format(LogLanguageManager.Instance.launchCustomization_killAfterCancelFailed.CurrentValue(), Environment.NewLine, exception));
            }

            throw;
        }

        Logger.Info(string.Format(LogLanguageManager.Instance.launchCustomization_commandExited.CurrentValue(), process.ExitCode));
        return process.ExitCode;
    }

    public static void RunShellCommandDetached(string command, string? workingDirectory)
    {
        Logger.Info(string.Format(LogLanguageManager.Instance.launchCustomization_commandScheduled.CurrentValue(),
            workingDirectory ?? CommonLanguageManager.Instance.launch_defaultDirectory.CurrentValue()));
        Task.Run(async () =>
        {
            try
            {
                await RunShellCommandAsync(command, workingDirectory, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Logger.Warning(string.Format(LogLanguageManager.Instance.launchCustomization_backgroundCommandFailed.CurrentValue(), Environment.NewLine, exception));
            }
        }).ContinueWith(completedTask => Logger.Error(string.Format(LogLanguageManager.Instance.launchCustomization_backgroundCommandFaulted.CurrentValue(), command), completedTask.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public static void WatchWindowTitle(Process process, string title)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(title))
            return;

        Task.Run(async () =>
        {
            Logger.Debug(string.Format(LogLanguageManager.Instance.launchCustomization_watchWindowTitleStart.CurrentValue(), process.Id));
            try
            {
                while (!process.HasExited)
                {
                    process.Refresh();
                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        var buffer = new StringBuilder(512);
                        _ = GetWindowText(handle, buffer, buffer.Capacity);
                        if (!string.Equals(buffer.ToString(), title, StringComparison.Ordinal))
                            _ = SetWindowText(handle, title);
                    }

                    await Task.Delay(1000);
                }
            }
            catch (Exception exception)
            {
                Logger.Debug(string.Format(LogLanguageManager.Instance.launchCustomization_watchWindowTitleStopped.CurrentValue(), process.Id, Environment.NewLine, exception));
            }
        }).ContinueWith(completedTask => Logger.Error(string.Format(LogLanguageManager.Instance.launchCustomization_watchWindowTitleFaulted.CurrentValue(), process.Id), completedTask.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
