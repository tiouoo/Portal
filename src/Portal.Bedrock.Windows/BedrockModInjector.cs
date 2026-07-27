using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Portal.Bedrock.Standard.Manifest;

namespace Portal.Bedrock;

internal static class BedrockModInjector
{
    private const string ResourceName = "Inject.dll";
    private const string DllName = "Inject.dll";
    private static readonly object SyncRoot = new();
    private static InjectDelegate? _inject;
    private static nint _module;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InjectDelegate(int processId, nint dllPath, [MarshalAs(UnmanagedType.I1)] bool delayInject,
        int delayMs);

    public static void Start(BedrockInstanceConfig config, Process process)
    {
        if (config.BuildType != BedrockBuildType.GDK)
            return;

        IReadOnlyList<BedrockModInfo> mods;
        try
        {
            mods = BedrockModManager.Scan(config)
                .Where(mod => mod.Config.Enabled && !mod.Config.Preload)
                .ToArray();
        }
        catch (Exception exception)
        {
            Trace.TraceError($"读取基岩版 DLL 模组失败：{exception}");
            return;
        }

        foreach (var mod in mods)
            _ = Task.Run(() => Inject(process, mod));
    }

    private static void Inject(Process process, BedrockModInfo mod)
    {
        try
        {
            if (process.HasExited)
                return;

            var inject = GetInjector();
            var runtimePath = PrepareRuntimeMod(mod);
            var path = Marshal.StringToHGlobalAnsi(runtimePath);
            try
            {
                var result = inject(process.Id, path, mod.Config.DelayMs > 0, mod.Config.DelayMs);
                if (result != 0)
                    Trace.TraceError($"注入基岩版模组失败：{mod.FileName}，返回值 {result}");
                else
                    Trace.TraceInformation($"已注入基岩版模组：{mod.FileName}");
            }
            finally
            {
                Marshal.FreeHGlobal(path);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError($"注入基岩版模组失败：{mod.FileName}，{exception}");
        }
    }

    private static string PrepareRuntimeMod(BedrockModInfo mod)
    {
        var portalFolder = Directory.GetParent(Path.GetDirectoryName(mod.FilePath)!)!.FullName;
        var runtimeFolder = Path.Combine(portalFolder, "runtime", "mods");
        Directory.CreateDirectory(runtimeFolder);
        using var stream = File.OpenRead(mod.FilePath);
        var fileName = $"{Convert.ToHexString(SHA256.HashData(stream))[..16]}.dll";
        var destination = Path.Combine(runtimeFolder, fileName);
        if (!File.Exists(destination))
            File.Copy(mod.FilePath, destination);
        return destination;
    }

    private static InjectDelegate GetInjector()
    {
        lock (SyncRoot)
        {
            if (_inject != null)
                return _inject;

            var nativeFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "xyz.tiouo.Portal", "Native");
            Directory.CreateDirectory(nativeFolder);
            var assembly = Assembly.GetExecutingAssembly();
            byte[] bytes;
            using (var stream = assembly.GetManifestResourceStream(ResourceName)
                                ?? throw new InvalidOperationException("未找到内嵌的基岩版模组注入组件。"))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                bytes = memory.ToArray();
            }

            var nativePath = Path.Combine(nativeFolder,
                $"Inject-{Convert.ToHexString(SHA256.HashData(bytes))[..16]}.dll");
            if (!File.Exists(nativePath))
                File.WriteAllBytes(nativePath, bytes);

            var module = NativeLibrary.Load(nativePath);
            try
            {
                var export = NativeLibrary.GetExport(module, "Inject");
                _inject = Marshal.GetDelegateForFunctionPointer<InjectDelegate>(export);
                _module = module;
            }
            catch
            {
                NativeLibrary.Free(module);
                throw;
            }
            return _inject;
        }
    }
}
