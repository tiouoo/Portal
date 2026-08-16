using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Portal.Bedrock.Preload;

/// <summary>
/// DLL 加载入口（等价原 C++ 的 DllMain）：DLL_PROCESS_ATTACH 时
/// 初始化运行时即执行模块初始器，完成工作目录、控制台、文件钩子与预加载调度。
/// </summary>
internal static unsafe class ModuleEntry
{
    private static readonly ConfigManager Config = new();
    private static bool _initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Logger.Error($"Module initialization failed: {ex}");
        }
    }

    private static void Run()
    {
        UseExeDirectoryAsWorkingDirectory();

        if (Config.GetConfigBool("isConsole"))
            OpenConsole();

        if (Config.GetConfigBool("isVersionIsolated"))
        {
            Logger.Info("Initializing File Hook.");
            FileRedirectHooks.Install(Config);
        }

        NativeExports.LogInjection();

        nint thread = NativeMethods.CreateThread(nint.Zero, 0,
            (nint)(delegate* unmanaged<nint, uint>)&WorkerThread, nint.Zero, 0, out _);
        if (thread != 0)
            NativeMethods.CloseHandle(thread);
    }

    [UnmanagedCallersOnly]
    private static uint WorkerThread(nint parameter)
    {
        try
        {
            bool console = Config.GetConfigBool("isConsole");
            Logger.Initialize(console, Config.GetConfig("nativeLogFile"));
            if (console)
            {
                PrintBanner();
                VersionInfo.Print();
                Logger.Success("Portal is free software licensed under GPLv3");
                Logger.Success("Submit issues and submit PR: https://github.com/tiouo/Portal");
            }
            PreloadLoader.Run();
        }
        catch (Exception ex)
        {
            Logger.Error($"Preload worker thread error: {ex}");
        }
        return 0;
    }

    private static void UseExeDirectoryAsWorkingDirectory()
    {
        string? directory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(directory))
            NativeMethods.SetCurrentDirectoryW(directory);
    }

    private static void OpenConsole()
    {
        try
        {
            NativeMethods.AllocConsole();
            NativeMethods.SetConsoleTitleW("Minecraft Bedrock Console");
        }
        catch
        {
        }
    }

    private static void PrintBanner()
    {
        const string banner = """
              ____                   _             _     __  __    ____
             |  _ \    ___    _ __  | |_    __ _  | |   |  \/  |  / ___|
             | |_) |  / _ \  | '__| | __|  / _` | | |   | |\/| | | |
             |  __/  | (_) | | |    | |_  | (_| | | |   |  |  | | |___
             |_|      \___/  |_|     \__|  \__,_| |_|   |_|  |_|  \____|

            """;

        foreach (string line in banner.Split('\n'))
            Logger.Info(line);
    }
}
