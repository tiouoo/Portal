using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Portal.Bedrock.Preload;

/// <summary>按预加载清单加载 preload 目录下的第三方 DLL。</summary>
internal static class PreloadLoader
{
    private const string XUserHook = "XUserHook.dll";

    public static void Run()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "preload");
        Directory.CreateDirectory(directory);
        Logger.Info("Loading DLLs from preload directory");

        int loaded = EnumerateCandidates(directory).Count(Load);
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
            if (!Path.GetFileName(file).Equals(XUserHook, StringComparison.OrdinalIgnoreCase))
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
        if (NativeMethods.LoadLibraryW(path) != 0)
        {
            Logger.Success($"Success for loading DLL: {name}");
            return true;
        }

        Logger.Error($"FAILED Error: {Marshal.GetLastWin32Error()}");
        return false;
    }
}
