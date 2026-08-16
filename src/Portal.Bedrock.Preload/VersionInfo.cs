using System.Runtime.InteropServices;
using System.Text;

namespace Portal.Bedrock.Preload;

/// <summary>DLL 版本与提交信息（导出为 UTF-8 常量字符串）。</summary>
internal static unsafe class VersionInfo
{
    private const string Version = "0.0.0";
    private const string Commit = "unknown";

    private static readonly nint _versionPtr = Pin(Version);
    private static readonly nint _commitPtr = Pin(Commit);

    internal static byte* DllVersion => (byte*)_versionPtr;
    internal static byte* CommitHash => (byte*)_commitPtr;

    internal static void Print()
    {
        Logger.Info($"PreLoadCpp Version: {Version}");
        Logger.Info($"Commit: {Commit}");
    }

    private static nint Pin(string value)
    {
        GCHandle handle = GCHandle.Alloc(Encoding.UTF8.GetBytes(value + '\0'), GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }
}
