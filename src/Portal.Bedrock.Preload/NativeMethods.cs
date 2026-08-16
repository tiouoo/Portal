using System.Runtime.InteropServices;

namespace Portal.Bedrock.Preload;

[StructLayout(LayoutKind.Sequential)]
internal struct SystemTime
{
    public ushort Year;
    public ushort Month;
    public ushort DayOfWeek;
    public ushort Day;
    public ushort Hour;
    public ushort Minute;
    public ushort Second;
    public ushort Milliseconds;
}

/// <summary>原生 Win32 互操作入口（LibraryImport 源码生成，AOT 友好）。</summary>
internal static unsafe partial class NativeMethods
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCurrentDirectoryW(string path);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetStdHandle(uint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteFile(nint file, byte* buffer, uint bytesToWrite, out uint bytesWritten, nint overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteConsoleW(nint console, char* buffer, uint charsToWrite, out uint charsWritten, nint reserved);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetConsoleTextAttribute(nint console, ushort attributes);

    [LibraryImport("kernel32.dll")]
    internal static partial void GetLocalTime(out SystemTime time);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandleW(string moduleName);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(nint module, string procName);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllocConsole();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetConsoleTitleW(string title);

    [LibraryImport("kernel32.dll")]
    internal static partial nint CreateThread(nint attributes, nuint stackSize, nint startAddress,
        nint parameter, uint creationFlags, out uint threadId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    internal static partial nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protect);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualProtect(nint address, nuint size, uint newProtect, out uint oldProtect);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualFree(nint address, nuint size, uint freeType);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint LoadLibraryW(string path);
}
