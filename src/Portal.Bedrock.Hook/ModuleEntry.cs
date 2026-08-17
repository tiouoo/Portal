using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Portal.Bedrock.Hook.Mods;
using Portal.Bedrock.Hook.Network;

namespace Portal.Bedrock.Hook;

/// <summary>
/// DLL 加载入口：NativeAOT 对 LoadLibraryW 加载的模块会延迟模块初始化，
/// 直到首次进入该模块。因此本模块同时提供 [ModuleInitializer]（首次进入时执行）
/// 与显式导出 HookInit（由 Portal.Preload.Net 在 LoadLibraryW 后调用），
/// 两者均只触发一次，确保工作线程被拉起。
/// </summary>
internal static unsafe class ModuleEntry
{
    private static int _initialized;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;
        RunStartup();
    }

    [UnmanagedCallersOnly(EntryPoint = "HookInit")]
    internal static int HookInit()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return 0;
        RunStartup();
        return 0;
    }

    private static void RunStartup()
    {
        WriteBootMarker("module-init-start");
        try
        {
            nint thread = NativeMethods.CreateThread(nint.Zero, 0,
                (nint)(delegate* unmanaged<nint, uint>)&WorkerThread, nint.Zero, 0, out _);
            if (thread != 0)
                NativeMethods.CloseHandle(thread);
            else
                XUserBridge.Warn($"CreateThread failed: {Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            XUserBridge.Warn($"Module initialization failed: {ex}");
        }
        WriteBootMarker("module-init-done");
    }

    private static void WriteBootMarker(string state)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Environment.ProcessPath);
            if (string.IsNullOrEmpty(directory))
                return;

            string path = Path.Combine(directory, "config", "Portal", "logs", "hook-boot.log");
            byte[] line = System.Text.Encoding.UTF8.GetBytes($"{DateTime.Now:O} {state}\n");
            nint handle = NativeMethods.CreateFileW(path, 0x40000000, 0x7, nint.Zero, 2, 0x80, nint.Zero);
            if (handle == -1)
                return;
            fixed (byte* buffer = line)
            {
                NativeMethods.WriteFile(handle, buffer, (uint)line.Length, out _, nint.Zero);
            }
            NativeMethods.CloseHandle(handle);
        }
        catch
        {
        }
    }

    private static nint GetSelfModuleBase()
    {
        try
        {
            return NativeMethods.GetModuleHandleW("XUserHook.dll");
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static uint WorkerThread(nint parameter)
    {
        string? gameDir = Directory.GetCurrentDirectory();
        if (string.IsNullOrEmpty(gameDir) && Environment.ProcessPath is { } path)
            gameDir = Path.GetDirectoryName(path);
        gameDir ??= string.Empty;

        HookLog.Initialize(gameDir);
        XUserBridge.LogSink = (message, ok) =>
        {
            if (ok)
                HookLog.Info(message);
            else
                HookLog.Warn(message);
        };

        XUserBridge.Info("XUserHook worker started");
        XUserBridge.Info($"XUserHook base=0x{GetSelfModuleBase():X} pid={Environment.ProcessId}");

        try
        {
            CrashReporter.Install();
        }
        catch (Exception ex)
        {
            XUserBridge.Warn($"CrashReporter install failed: {ex.Message}");
        }

        XUserBridge.Initialize();

        try
        {
            NetworkHookConfig.Start(gameDir);
            WinSock2Hook.Install();
        }
        catch (Exception ex)
        {
            XUserBridge.Warn($"Network hooks setup failed: {ex.Message}");
        }

        try
        {
            ModLoader.LoadMods(gameDir);
        }
        catch (Exception ex)
        {
            XUserBridge.Warn($"Mod loader failed: {ex.Message}");
        }

        return 0;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern nint CreateThread(nint attributes, nuint stackSize, nint startAddress, nint parameter, uint creationFlags, out uint threadId);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern int CloseHandle(nint handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        internal static extern nint CreateFileW(string name, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern nint GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern int WriteFile(nint file, byte* buffer, uint bytesToWrite, out uint bytesWritten, nint overlapped);
    }
}
