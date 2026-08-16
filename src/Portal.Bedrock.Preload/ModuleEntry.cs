using System;
using System.IO;
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
    private static readonly nint InvalidHandle = new(-1);
    private static bool _initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        WriteBootMarker("module-init-start");
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            Logger.Error($"Module initialization failed: {ex}");
        }
        WriteBootMarker("module-init-done");
    }

    /// <summary>
    /// 极简启动标记：仅用原生句柄写入，不经过托管文件层（降低 DllMain 期间 loader 锁死锁风险）。
    /// 用于区分"DLL 未被加载"与"加载后初始化中途失败"。
    /// </summary>
    private static void WriteBootMarker(string state)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(directory))
                return;

            string path = Path.Combine(directory, "config", "Portal", "logs", "boot.log");
            byte[] line = System.Text.Encoding.UTF8.GetBytes($"{DateTime.Now:O} {state}\n");
            nint handle = NativeMethods.CreateFileW(path, 0x40000000 /* GENERIC_WRITE */,
                0x7 /* SHARE_READ|WRITE|DELETE */, nint.Zero, 2 /* CREATE_ALWAYS */,
                0x80 /* FILE_ATTRIBUTE_NORMAL */, nint.Zero);
            if (handle == InvalidHandle)
                return;

            unsafe
            {
                fixed (byte* buffer = line)
                {
                    NativeMethods.WriteFile(handle, buffer, (uint)line.Length, out _, nint.Zero);
                }
            }
            NativeMethods.CloseHandle(handle);
        }
        catch
        {
        }
    }

    private static void Run()
    {
        try
        {
            UseExeDirectoryAsWorkingDirectory();
        }
        catch (Exception ex)
        {
            Logger.Error($"Set working directory failed: {ex}");
        }

        try
        {
            if (Config.GetConfigBool("isConsole"))
                OpenConsole();
        }
        catch
        {
        }

        try
        {
            if (Config.GetConfigBool("isVersionIsolated"))
            {
                Logger.Info("Initializing File Hook.");
                FileRedirectHooks.Install(Config);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"File hook install failed: {ex}");
        }

        NativeExports.LogInjection();

        nint thread = NativeMethods.CreateThread(nint.Zero, 0,
            (nint)(delegate* unmanaged<nint, uint>)&WorkerThread, nint.Zero, 0, out _);
        if (thread != 0)
            NativeMethods.CloseHandle(thread);
        else
            Logger.Error($"CreateThread failed: {Marshal.GetLastWin32Error()}");
    }

    [UnmanagedCallersOnly]
    private static uint WorkerThread(nint parameter)
    {
        try
        {
            bool console = Config.GetConfigBool("isConsole");
            Logger.Initialize(console, Config.GetConfig("nativeLogFile"));
            Logger.Info($"Preload worker started. Exe: {Environment.ProcessPath} CWD: {Environment.CurrentDirectory}");
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
