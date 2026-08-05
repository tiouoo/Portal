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
using Portal.Bedrock.Standard.Interface;

namespace Portal.Bedrock;

internal static class BedrockModInjector
{
    private const string ResourceName = "Inject.dll";
    private const string DllName = "Inject.dll";
    private static readonly object SyncRoot = new();
    private static InjectDelegate? _inject;
    private static nint _module;

    private static string GetInjectError(int result) => result switch
    {
        -1 => "参数无效",
        -2 => "DLL 路径不是存在的绝对文件路径",
        -3 => "无法打开目标进程",
        -4 => "无法解析 LoadLibraryA",
        -5 => "无法在目标进程分配内存",
        -6 => "无法向目标进程写入 DLL 路径",
        -7 => "无法创建远程线程",
        -8 => "等待远程线程或加载 DLL 失败",
        _ => $"未知错误 ({result})"
    };

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InjectDelegate(int processId, nint dllPath, [MarshalAs(UnmanagedType.I1)] bool delayInject,
        int delayMs);

    public static void Start(BedrockInstanceConfig config, Process process,
        Action<string, BedrockLogLevel>? log = null)
    {
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
            log?.Invoke($"读取 DLL 模组失败：{exception}", BedrockLogLevel.Error);
            return;
        }

        log?.Invoke($"发现 {mods.Count} 个等待注入的模组", BedrockLogLevel.Information);
        foreach (var mod in mods)
        {
            log?.Invoke($"已安排模组注入：{mod.FileName}，延迟 {mod.Config.DelayMs} ms",
                BedrockLogLevel.Information);
            _ = Task.Run(() => Inject(process, mod, log)); // Inject records every terminal failure.
        }
    }

    private static void Inject(Process process, BedrockModInfo mod, Action<string, BedrockLogLevel>? log)
    {
        try
        {
            if (process.HasExited)
            {
                log?.Invoke($"已跳过模组 {mod.FileName}：Minecraft 进程已经退出", BedrockLogLevel.Warning);
                return;
            }

            var inject = GetInjector();
            var runtimePath = PrepareRuntimeMod(mod);
            var path = Marshal.StringToHGlobalAnsi(runtimePath);
            try
            {
                var result = inject(process.Id, path, mod.Config.DelayMs > 0, mod.Config.DelayMs);
                if (result != 0)
                {
                    var error = GetInjectError(result);
                    Trace.TraceError($"Portal 注入基岩版模组失败：{mod.FileName}，{error}，返回值 {result}");
                    log?.Invoke($"模组注入失败：{mod.FileName}，{error}（{result}）", BedrockLogLevel.Error);
                }
                else
                {
                    Trace.TraceInformation($"Portal 已注入基岩版模组：{mod.FileName}");
                    log?.Invoke($"模组注入成功：{mod.FileName}", BedrockLogLevel.Information);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(path);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError($"Portal 注入基岩版模组失败：{mod.FileName}，{exception}");
            log?.Invoke($"模组注入异常：{mod.FileName}，{exception}", BedrockLogLevel.Error);
        }
    }

    private static string PrepareRuntimeMod(BedrockModInfo mod)
    {
        var portalFolder = Directory.GetParent(Path.GetDirectoryName(mod.FilePath)!)!.FullName;
        var runtimeFolder = Path.Combine(portalFolder, "runtime", "mods");
        Trace.TraceInformation($"准备基岩版模组运行时副本：{mod.FilePath} -> {runtimeFolder}。");
        Directory.CreateDirectory(runtimeFolder);
        using var stream = File.OpenRead(mod.FilePath);
        var fileName = $"{Convert.ToHexString(SHA256.HashData(stream))[..16]}.dll";
        var destination = Path.GetFullPath(Path.Combine(runtimeFolder, fileName));
        if (!File.Exists(destination))
        {
            Trace.TraceInformation($"复制基岩版模组到运行时目录：{destination}。");
            File.Copy(mod.FilePath, destination);
        }
        return destination;
    }

    private static InjectDelegate GetInjector()
    {
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Portal 基岩版模组注入器仅支持 x64 进程。");

        lock (SyncRoot)
        {
            if (_inject != null)
                return _inject;

            var nativeFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "cc.tiouo.Portal", "Native");
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
            catch (Exception exception)
            {
                Trace.TraceError($"加载基岩版模组注入器失败。{Environment.NewLine}{exception}");
                NativeLibrary.Free(module);
                throw;
            }
            return _inject;
        }
    }
}
