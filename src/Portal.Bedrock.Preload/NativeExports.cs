using System.Runtime.InteropServices;

namespace Portal.Bedrock.Preload;

/// <summary>以原生符号导出的入口，供 PE 导入表 / 外部加载调用。</summary>
internal static unsafe class NativeExports
{
    internal static void LogInjection() => Logger.Info("Portal Injecting!");

    [UnmanagedCallersOnly(EntryPoint = "Load")]
    internal static void Load()
    {
        PreloadLoader.LoadPriorityPreloader();
        ModuleEntry.StartWorker();
        LogInjection();
    }

    [UnmanagedCallersOnly(EntryPoint = "GetDllVersion")]
    internal static byte* GetDllVersion() => VersionInfo.DllVersion;

    [UnmanagedCallersOnly(EntryPoint = "GetCommitHash")]
    internal static byte* GetCommitHash() => VersionInfo.CommitHash;
}
