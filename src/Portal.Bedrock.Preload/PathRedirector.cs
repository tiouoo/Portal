using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Portal.Bedrock.Preload;

/// <summary>
/// 把 Minecraft Bedrock 的 AppData 路径重定向到隔离目录。
/// 支持 shares / independence / portal 三种隔离策略。
/// </summary>
internal static class PathRedirector
{
    private static readonly string[] Keywords =
    [
        @"AppData\Roaming\Minecraft Bedrock",
        @"AppData\Local\Packages\Microsoft.MinecraftUWP_8wekyb3d8bbwe",
        @"AppData\Local\Packages\Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe",
        @"AppData\Local\Packages\Microsoft.MinecraftUWP_8wekyb3d8bbwe\LocalState",
        @"AppData\Local\Packages\Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe\LocalState",
        @"AppData\Roaming\Minecraft Bedrock Preview",
    ];

    private static readonly HashSet<string> ExcludedTopLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "LocalCache", "SystemAppData", "Settings", "TempState", "RoamingState",
    };

    private const uint FileListDirectory = 0x0001;
    private const uint ShareReadWriteDelete = 0x0007;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;

    private static readonly Lock HandleLock = new();
    private static nint _rootHandle = InvalidHandle;
    private static nint InvalidHandle => new(-1);

    /// <summary>隔离根目录（按 folderPolicy 决定）。</summary>
    public static string BaseDirectory { get; private set; } = string.Empty;

    /// <summary>independence 策略下的相对隔离子目录。</summary>
    public static string IsolationFolder { get; } = @"config/Portal/isolation";

    /// <summary>计算需要重定向的相对路径；不匹配或命中排除项时返回空。</summary>
    public static string GetRedirectedRelativePath(string path)
    {
        foreach (string keyword in Keywords)
        {
            int at = path.IndexOf(keyword, StringComparison.Ordinal);
            if (at < 0)
                continue;

            string relative = path[(at + keyword.Length)..].TrimStart('\\', '/');
            if (relative.Length == 0)
                return string.Empty;

            relative = relative.Replace('/', '\\');
            int slash = relative.IndexOf('\\');
            string topLevel = slash >= 0 ? relative[..slash] : relative;

            if (ExcludedTopLevels.Contains(topLevel))
                return string.Empty;

            EnsureParentDirectory(Path.Combine(BaseDirectory, relative));
            return relative;
        }

        return string.Empty;
    }

    /// <summary>获取隔离根目录句柄（惰性打开并缓存）。</summary>
    public static nint GetRootHandle(ConfigManager config)
    {
        lock (HandleLock)
        {
            if (_rootHandle != InvalidHandle)
                return _rootHandle;

            InitializeBaseDirectory(config);
            _rootHandle = NativeMethods.CreateFileW(BaseDirectory, FileListDirectory, ShareReadWriteDelete,
                nint.Zero, OpenExisting, BackupSemantics, nint.Zero);
            return _rootHandle;
        }
    }

    public static bool IsDirectory(string relativePath) =>
        relativePath.Length == 0 || Directory.Exists(Path.Combine(BaseDirectory, relativePath));

    private static void InitializeBaseDirectory(ConfigManager config)
    {
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";

        BaseDirectory = config.GetConfig("folderPolicyString") switch
        {
            "shares" => config.GetInfoInt("versionType") switch
            {
                0 or 2 => Path.Combine(exeDir, "Minecraft Bedrock Preview"),
                1 => Path.Combine(exeDir, "Minecraft Bedrock"),
                _ => BaseDirectory,
            },
            "independence" or "" => Path.Combine(exeDir, IsolationFolder),
            "portal" => ResolvePortalDirectory(exeDir),
            _ => BaseDirectory,
        };

        if (!string.IsNullOrEmpty(BaseDirectory))
        {
            try
            {
                Directory.CreateDirectory(BaseDirectory);
            }
            catch (Exception ex)
            {
                Logger.Error($"Create isolation base dir failed: {BaseDirectory}: {ex.Message}");
            }
        }
    }

    private static string ResolvePortalDirectory(string exeDir)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrEmpty(appData)
            ? Path.Combine(exeDir, IsolationFolder)
            : Path.Combine(appData, "cc.tiouo.Portal", "Bedrock");
    }

    private static void EnsureParentDirectory(string fullPath)
    {
        string? parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent) || Directory.Exists(parent))
            return;

        try
        {
            Directory.CreateDirectory(parent);
        }
        catch
        {
        }
    }
}
