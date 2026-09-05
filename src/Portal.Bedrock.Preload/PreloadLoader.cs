using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Portal.Bedrock.Preload;

/// <summary>按预加载清单加载 preload 目录下的第三方 DLL。</summary>
internal static unsafe class PreloadLoader
{
    private const string PreLoader = "PreLoader.dll";
    private const string XUserHook = "Portal.XUserHook.dll";
    private static int _priorityPreloaderState;

    public static bool LoadPriorityPreloader()
    {
        if (Interlocked.CompareExchange(ref _priorityPreloaderState, 1, 0) != 0)
            return Volatile.Read(ref _priorityPreloaderState) == 2;

        // LeviLamina's official client installation statically imports
        // PreLoader.dll with PeEditor. Loading it again after the game entry
        // point can block inside its bootstrap initialization, so treat the
        // already-loaded module as the priority preloader.
        if (NativeMethods.GetModuleHandleW(PreLoader) != 0)
        {
            Logger.Info("PreLoader.dll is already loaded by the game import table");
            Volatile.Write(ref _priorityPreloaderState, 2);
            return true;
        }

        string path = Path.Combine(Directory.GetCurrentDirectory(), "preload", PreLoader);
        bool loaded = File.Exists(path) && Load(path);
        Volatile.Write(ref _priorityPreloaderState, loaded ? 2 : -1);
        return loaded;
    }

    public static void Run()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "preload");
        Directory.CreateDirectory(directory);
        Logger.Info("Loading DLLs from preload directory");
        bool priorityLoaded = LoadPriorityPreloader();

        int loaded = EnumerateCandidates(directory).Count(Load) + (priorityLoaded ? 1 : 0);
        Logger.Success($"Successfully loaded {loaded} DLL(s)");
    }

    private static IEnumerable<string> EnumerateCandidates(string directory)
    {
        string xUserHookPath = Path.Combine(directory, XUserHook);
        if (File.Exists(xUserHookPath))
        {
            Logger.Info("Loading Portal Xbox account hook first");
            yield return xUserHookPath;
        }
        else
        {
            Logger.Info("Portal Xbox account hook is not present; using the default Xbox session");
        }

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);
            if (!name.Equals(XUserHook, StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(PreLoader, StringComparison.OrdinalIgnoreCase))
                yield return file;
        }

        string manifest = Path.Combine(directory, "Portal", "mods.txt");
        if (!File.Exists(manifest))
            yield break;

        foreach (string line in File.ReadLines(manifest))
        {
            string name = line.Trim();
            if (name.Length > 0 && Path.GetFileName(name) == name)
                yield return Path.Combine(directory, "Portal", name);
        }
    }

    private static bool Load(string path)
    {
        if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            return false;

        string name = Path.GetFileName(path);
        Logger.Info($"Loading DLL: {name}...");
        nint module = NativeMethods.LoadLibraryW(path);
        if (module == 0)
        {
            Logger.Error($"FAILED Error: {Marshal.GetLastWin32Error()}");
            return false;
        }

        Logger.Success($"Success for loading DLL: {name}");
        if (name.Equals(XUserHook, StringComparison.OrdinalIgnoreCase))
            TriggerXUserHook(module);
        return true;
    }

    /// <summary>
    /// NativeAOT 共享库经 LoadLibraryW 加载后，模块初始化会延迟到首次进入该模块。
    /// 调用其 HookInit 导出以触发 XUser 会话接收与 QueryApiImpl Hook 装配。
    /// </summary>
    private static void TriggerXUserHook(nint module)
    {
        nint hookInit = NativeMethods.GetProcAddress(module, "HookInit");
        if (hookInit == 0)
        {
            Logger.Warning($"Portal.XUserHook.dll 未导出 HookInit；跳过触发");
            return;
        }

        Logger.Info("Calling Portal.XUserHook!HookInit");
        ((delegate* unmanaged<int>)hookInit)();
    }
}
