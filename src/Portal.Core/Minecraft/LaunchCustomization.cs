using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MinecraftLaunch.Base.Models.Authentication;
using MinecraftLaunch.Base.Models.Game;
using MinecraftLaunch.Extensions;
using Portal.Core.Minecraft.Classes;
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
            ? entry.ToWorkingPath(independent)
            : instance.InstanceFolderPath;
        var loaders = entry is ModifiedMinecraftEntry modified
            ? string.Join(" ", modified.ModLoaders.Select(loader => $"{loader.Type} {loader.Version}".Trim()))
            : string.Empty;

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{instance_name}"] = instance.InstanceName,
            ["{instance_id}"] = entry?.Id ?? instance.BedrockConfig?.Name ?? string.Empty,
            ["{instance_path}"] = instance.MinecraftPath ?? string.Empty,
            ["{game_dir}"] = gameDirectory ?? string.Empty,
            ["{minecraft_folder}"] = entry?.MinecraftFolderPath ?? instance.FolderPath ?? string.Empty,
            ["{natives_dir}"] = entry?.NativesDirectoryPath ?? string.Empty,
            ["{assets_dir}"] = entry?.AssetsDirectoryPath ?? string.Empty,
            ["{version}"] = entry?.Version.VersionId ?? instance.BedrockConfig?.Version ?? string.Empty,
            ["{version_type}"] = entry?.Version.Type.ToString() ?? string.Empty,
            ["{loader}"] = loaders,
            ["{edition}"] = instance.Type == MinecraftInstanceType.Bedrock ? "bedrock" : "java",
            ["{player_name}"] = account?.Name ?? options.Account?.Name ?? string.Empty,
            ["{player_uuid}"] = account?.Uuid.ToString() ?? options.Account?.Uuid?.ToString() ?? string.Empty,
            ["{account_type}"] = account?.Type.ToString() ?? options.Account?.AccountType.ToString() ?? string.Empty,
            ["{access_token}"] = account?.AccessToken ?? string.Empty,
            ["{java_path}"] = java?.JavaPath ?? string.Empty,
            ["{java_dir}"] = Path.GetDirectoryName(java?.JavaPath) ?? string.Empty,
            ["{java_version}"] = java?.JavaVersion ?? string.Empty,
            ["{java_major}"] = java?.MajorVersion.ToString() ?? string.Empty,
            ["{width}"] = options.WindowWidth.ToString(),
            ["{height}"] = options.WindowHeight.ToString(),
            ["{max_memory}"] = options.MaxMemory.ToString(),
            ["{launcher_dir}"] = AppContext.BaseDirectory,
            ["{launcher_path}"] = Environment.ProcessPath ?? string.Empty,
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
        {
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
        }

        if (current.Length > 0)
            arguments.Add(current.ToString());
        return arguments;
    }

        public static async Task<int> RunShellCommandAsync(string command, string? workingDirectory,
        CancellationToken cancellationToken)
    {
        Logger.Info($"执行启动自定义命令，工作目录：{workingDirectory ?? "默认目录"}");
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { Arguments = $"/c {command}" }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", command } };
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        Logger.Debug($"启动自定义命令进程：{process.Id}");
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception)
            {
                Logger.Debug($"取消自定义命令后终止进程失败，进程可能已退出。{Environment.NewLine}{exception}");
            }
            throw;
        }
        Logger.Info($"启动自定义命令已结束，退出代码：{process.ExitCode}");
        return process.ExitCode;
    }

        public static void RunShellCommandDetached(string command, string? workingDirectory)
    {
        Logger.Info($"已安排后台启动自定义命令，工作目录：{workingDirectory ?? "默认目录"}");
        Task.Run(async () =>
        {
            try
            {
                await RunShellCommandAsync(command, workingDirectory, CancellationToken.None);
            }
            catch (Exception exception)
            {
                
                Logger.Warning($"后台启动自定义命令失败。{Environment.NewLine}{exception}");
            }
        }).ContinueWith(completedTask => Logger.Error($"后台启动自定义命令异常结束：{command}", completedTask.Exception!),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

        public static void WatchWindowTitle(Process process, string title)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(title))
            return;

        Task.Run(async () =>
        {
            Logger.Debug($"开始监视游戏窗口标题，进程：{process.Id}");
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
                
                Logger.Debug($"停止监视游戏窗口标题，进程：{process.Id}。{Environment.NewLine}{exception}");
            }
        }).ContinueWith(completedTask => Logger.Error($"游戏窗口标题监视异常结束，进程：{process.Id}", completedTask.Exception!),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
