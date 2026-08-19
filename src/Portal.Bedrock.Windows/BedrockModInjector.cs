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
using Portal.Localization;

namespace Portal.Bedrock;

internal static class BedrockModInjector
{
    private const string ResourceName = "Portal.Inject.dll";
    private const string DllName = "Portal.Inject.dll";
    private static readonly object SyncRoot = new();
    private static InjectDelegate? _inject;
    private static nint _module;

    private static string GetInjectError(int result) => result switch
    {
        -1 => LogLanguageManager.Instance.bedrock_injectErrorInvalidArgument.CurrentValue(),
        -2 => LogLanguageManager.Instance.bedrock_injectErrorDllNotAbsolute.CurrentValue(),
        -3 => LogLanguageManager.Instance.bedrock_injectErrorCannotOpenProcess.CurrentValue(),
        -4 => LogLanguageManager.Instance.bedrock_injectErrorCannotResolveLoadLibrary.CurrentValue(),
        -5 => LogLanguageManager.Instance.bedrock_injectErrorCannotAllocateMemory.CurrentValue(),
        -6 => LogLanguageManager.Instance.bedrock_injectErrorCannotWriteDllPath.CurrentValue(),
        -7 => LogLanguageManager.Instance.bedrock_injectErrorCannotCreateRemoteThread.CurrentValue(),
        -8 => LogLanguageManager.Instance.bedrock_injectErrorRemoteThreadFailed.CurrentValue(),
        _ => string.Format(LogLanguageManager.Instance.bedrock_injectErrorUnknown.CurrentValue(), result)
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
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_readDllModsFailed.CurrentValue(), exception));
            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_readDllModsFailedShort.CurrentValue(), exception), BedrockLogLevel.Error);
            return;
        }

        log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_foundPendingInjectMods.CurrentValue(), mods.Count), BedrockLogLevel.Information);
        foreach (var mod in mods)
        {
            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_modInjectScheduled.CurrentValue(), mod.FileName, mod.Config.DelayMs),
                BedrockLogLevel.Information);
            _ = Task.Run(() => Inject(process, mod, log)); 
        }
    }

    private static void Inject(Process process, BedrockModInfo mod, Action<string, BedrockLogLevel>? log)
    {
        try
        {
            if (process.HasExited)
            {
                log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_modInjectSkippedProcessExited.CurrentValue(), mod.FileName), BedrockLogLevel.Warning);
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
                    Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_modInjectFailedWithCode.CurrentValue(), mod.FileName, error, result));
                    log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_modInjectFailed.CurrentValue(), mod.FileName, error, result), BedrockLogLevel.Error);
                }
                else
                {
                    Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrock_modInjected.CurrentValue(), mod.FileName));
                    log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_modInjectSuccess.CurrentValue(), mod.FileName), BedrockLogLevel.Information);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(path);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_modInjectException.CurrentValue(), mod.FileName, exception));
            log?.Invoke(string.Format(LogLanguageManager.Instance.bedrock_modInjectExceptionShort.CurrentValue(), mod.FileName, exception), BedrockLogLevel.Error);
        }
    }

    private static string PrepareRuntimeMod(BedrockModInfo mod)
    {
        var portalFolder = Directory.GetParent(Path.GetDirectoryName(mod.FilePath)!)!.FullName;
        var runtimeFolder = Path.Combine(portalFolder, "runtime", "mods");
        Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrock_preparingModRuntimeCopy.CurrentValue(), mod.FilePath, runtimeFolder));
        Directory.CreateDirectory(runtimeFolder);
        using var stream = File.OpenRead(mod.FilePath);
        var fileName = $"{Convert.ToHexString(SHA256.HashData(stream))[..16]}.dll";
        var destination = Path.GetFullPath(Path.Combine(runtimeFolder, fileName));
        if (!File.Exists(destination))
        {
            Trace.TraceInformation(string.Format(LogLanguageManager.Instance.bedrock_copyingModToRuntime.CurrentValue(), destination));
            File.Copy(mod.FilePath, destination);
        }
        return destination;
    }

    private static InjectDelegate GetInjector()
    {
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException(CommonLanguageManager.Instance.bedrock_modInjectorX64Only.CurrentValue());

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
                                ?? throw new InvalidOperationException(CommonLanguageManager.Instance.bedrock_missingEmbeddedInjector.CurrentValue()))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                bytes = memory.ToArray();
            }

            var nativePath = Path.Combine(nativeFolder,
                $"Portal.Inject-{Convert.ToHexString(SHA256.HashData(bytes))[..16]}.dll");
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
                Trace.TraceError(string.Format(LogLanguageManager.Instance.bedrock_loadModInjectorFailed.CurrentValue(), Environment.NewLine, exception));
                NativeLibrary.Free(module);
                throw;
            }
            return _inject;
        }
    }
}
