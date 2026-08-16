using System;
using System.Runtime.InteropServices;

namespace Portal.Bedrock.Preload;

/// <summary>
/// 挂钩 ntdll 文件相关 API，把基岩版存档访问重定向到隔离目录。
/// </summary>
internal static unsafe partial class FileRedirectHooks
{
    private const int BufferChars = 2048;
    private static readonly nint InvalidHandle = new(-1);

    private static ConfigManager? _config;
    private static bool _detailedLog;

    private static nint _createFile;
    private static nint _openFile;
    private static nint _queryAttributes;
    private static nint _queryFullAttributes;
    private static nint _setInformation;
    private static nint _deleteFile;
    private static nint _queryDirectory;
    private static nint _createSection;

    public static void Install(ConfigManager config)
    {
        _config = config;
        _detailedLog = config.GetConfigBool("isDetailedLog");

        nint ntdll = NativeMethods.GetModuleHandleW("ntdll.dll");
        if (ntdll == 0)
        {
            Logger.Error("Get ntdll pt error");
            return;
        }

        _createFile = NativeMethods.GetProcAddress(ntdll, "NtCreateFile");
        _openFile = NativeMethods.GetProcAddress(ntdll, "NtOpenFile");
        _queryAttributes = NativeMethods.GetProcAddress(ntdll, "NtQueryAttributesFile");
        _queryFullAttributes = NativeMethods.GetProcAddress(ntdll, "NtQueryFullAttributesFile");
        _setInformation = NativeMethods.GetProcAddress(ntdll, "NtSetInformationFile");
        _deleteFile = NativeMethods.GetProcAddress(ntdll, "NtDeleteFile");
        _queryDirectory = NativeMethods.GetProcAddress(ntdll, "NtQueryDirectoryFile");
        _createSection = NativeMethods.GetProcAddress(ntdll, "NtCreateSection");

        LogAddresses();

        int attached = 0;
        attached += Attach(ref _createFile, Detour(CreateFileHook)) ? 1 : 0;
        attached += Attach(ref _openFile, Detour(OpenFileHook)) ? 1 : 0;
        attached += Attach(ref _queryAttributes, Detour(QueryAttributesHook)) ? 1 : 0;
        attached += Attach(ref _queryFullAttributes, Detour(QueryFullAttributesHook)) ? 1 : 0;
        attached += Attach(ref _setInformation, Detour(SetInformationHook)) ? 1 : 0;
        attached += Attach(ref _deleteFile, Detour(DeleteFileHook)) ? 1 : 0;
        attached += Attach(ref _queryDirectory, Detour(QueryDirectoryHook)) ? 1 : 0;
        attached += Attach(ref _createSection, Detour(CreateSectionHook)) ? 1 : 0;

        Logger.Success(attached == 8
            ? "File Redirector Hooked Successfully. Attached: 8"
            : $"Detour attach incomplete. Attached: {attached}/8");
    }

    private static void LogAddresses()
    {
        Logger.Info($"NtCreateFile addr: 0x{_createFile:X}");
        Logger.Info($"NtOpenFile addr: 0x{_openFile:X}");
        Logger.Info($"NtQueryAttributesFile addr: 0x{_queryAttributes:X}");
        Logger.Info($"NtQueryFullAttributesFile addr: 0x{_queryFullAttributes:X}");
        Logger.Info($"NtSetInformationFile addr: 0x{_setInformation:X}");
        Logger.Info($"NtDeleteFile addr: 0x{_deleteFile:X}");
        Logger.Info($"NtQueryDirectoryFile addr: 0x{_queryDirectory:X}");
        Logger.Info($"NtCreateSection addr: 0x{_createSection:X}");
    }

    private static bool Attach(ref nint original, nint detour)
    {
        if (!InlineHook.TryCreate(original, detour, out nint trunk))
            return false;
        original = trunk;
        return true;
    }

    private static nint Detour(void* hook) => (nint)hook;

    // ---- 各 hook 的类型化原始函数指针（用于回传 trampoline 地址）----

    private static delegate* unmanaged<nint*, uint, ObjectAttributes*, IoStatusBlock*, long*, uint, uint, uint, uint, void*, uint, int> CreateFileHook => &OnCreateFile;
    private static delegate* unmanaged<nint*, uint, ObjectAttributes*, IoStatusBlock*, uint, uint, int> OpenFileHook => &OnOpenFile;
    private static delegate* unmanaged<ObjectAttributes*, void*, int> QueryAttributesHook => &OnQueryAttributes;
    private static delegate* unmanaged<ObjectAttributes*, void*, int> QueryFullAttributesHook => &OnQueryFullAttributes;
    private static delegate* unmanaged<nint, IoStatusBlock*, void*, uint, FileInformationClass, int> SetInformationHook => &OnSetInformation;
    private static delegate* unmanaged<ObjectAttributes*, int> DeleteFileHook => &OnDeleteFile;
    private static delegate* unmanaged<nint, nint, void*, void*, IoStatusBlock*, void*, uint, FileInformationClass, byte, UnicodeString*, byte, int> QueryDirectoryHook => &OnQueryDirectory;
    private static delegate* unmanaged<nint*, uint, ObjectAttributes*, long*, uint, uint, nint, int> CreateSectionHook => &OnCreateSection;

    /// <summary>构造重定向后的 OBJECT_ATTRIBUTES；不命中时返回 false。</summary>
    private static bool TryRedirect(
        ObjectAttributes* attributes, ObjectAttributes* patched, UnicodeString* name, char* buffer,
        string operation, out string? relative)
    {
        relative = null;
        if (attributes is null || attributes->ObjectName is null || attributes->ObjectName->Buffer is null)
            return false;

        string path = new(attributes->ObjectName->Buffer, 0, attributes->ObjectName->Length / 2);
        if (_detailedLog)
            Logger.WriteFromHook(LogLevel.Info, $"{operation}: {path}");

        string redirect = PathRedirector.GetRedirectedRelativePath(path);
        if (redirect.Length == 0)
            return false;

        nint root = PathRedirector.GetRootHandle(_config!);
        if (root == InvalidHandle)
            return false;

        redirect.AsSpan().CopyTo(new Span<char>(buffer, redirect.Length));
        buffer[redirect.Length] = '\0';

        *name = new UnicodeString
        {
            Length = (ushort)(redirect.Length * 2),
            MaximumLength = (ushort)((redirect.Length + 1) * 2),
            Buffer = buffer,
        };

        *patched = *attributes;
        patched->Attributes = NtConstants.DontReparse;
        patched->ObjectName = name;
        patched->RootDirectory = root;
        patched->SecurityDescriptor = nint.Zero;

        relative = redirect;
        return true;
    }

    private static uint AsDirectoryFlag(uint options, string relative) =>
        PathRedirector.IsDirectory(relative) || relative.EndsWith('\\')
            ? (options & ~NtConstants.NonDirectoryFile) | NtConstants.DirectoryFile
            : options;

    [UnmanagedCallersOnly]
    private static int OnCreateFile(nint* handle, uint access, ObjectAttributes* attributes, IoStatusBlock* io,
        long* allocation, uint fileAttributes, uint share, uint disposition, uint options, void* ea, uint eaLength)
    {
        ObjectAttributes patched = default;
        UnicodeString name = default;
        char* buffer = stackalloc char[BufferChars];

        bool redirected = TryRedirect(attributes, &patched, &name, buffer, "NtCreateFile", out string? relative);
        if (redirected)
            options = AsDirectoryFlag(options, relative!);

        return ((delegate* unmanaged<nint*, uint, ObjectAttributes*, IoStatusBlock*, long*, uint, uint, uint, uint, void*, uint, int>)_createFile)(
            handle, access, redirected ? &patched : attributes, io, allocation, fileAttributes, share, disposition, options, ea, eaLength);
    }

    [UnmanagedCallersOnly]
    private static int OnOpenFile(nint* handle, uint access, ObjectAttributes* attributes, IoStatusBlock* io,
        uint share, uint options)
    {
        ObjectAttributes patched = default;
        UnicodeString name = default;
        char* buffer = stackalloc char[BufferChars];

        bool redirected = TryRedirect(attributes, &patched, &name, buffer, "NtOpenFile", out string? relative);
        if (redirected)
            options = AsDirectoryFlag(options, relative!);

        return ((delegate* unmanaged<nint*, uint, ObjectAttributes*, IoStatusBlock*, uint, uint, int>)_openFile)(
            handle, access, redirected ? &patched : attributes, io, share, options);
    }

    [UnmanagedCallersOnly]
    private static int OnQueryAttributes(ObjectAttributes* attributes, void* fileInformation)
    {
        ObjectAttributes patched = default;
        UnicodeString name = default;
        char* buffer = stackalloc char[BufferChars];

        bool redirected = TryRedirect(attributes, &patched, &name, buffer, "NtQueryAttributesFile", out _);
        return ((delegate* unmanaged<ObjectAttributes*, void*, int>)_queryAttributes)(
            redirected ? &patched : attributes, fileInformation);
    }

    [UnmanagedCallersOnly]
    private static int OnQueryFullAttributes(ObjectAttributes* attributes, void* fileInformation)
    {
        ObjectAttributes patched = default;
        UnicodeString name = default;
        char* buffer = stackalloc char[BufferChars];

        bool redirected = TryRedirect(attributes, &patched, &name, buffer, "NtQueryFullAttributesFile", out _);
        return ((delegate* unmanaged<ObjectAttributes*, void*, int>)_queryFullAttributes)(
            redirected ? &patched : attributes, fileInformation);
    }

    [UnmanagedCallersOnly]
    private static int OnSetInformation(nint handle, IoStatusBlock* io, void* information, uint length,
        FileInformationClass fileClass)
    {
        if (fileClass is FileInformationClass.FileRenameInformation or FileInformationClass.FileRenameInformationEx)
        {
            byte* info = (byte*)information;
            if (info is not null)
            {
                uint nameLength = *(uint*)(info + 16);
                if (nameLength > 0)
                {
                    string original = new((char*)(info + NtConstants.RenameFileNameOffset), 0, (int)(nameLength / 2));
                    string relative = PathRedirector.GetRedirectedRelativePath(original);
                    if (relative.Length > 0)
                    {
                        nint root = PathRedirector.GetRootHandle(_config!);
                        if (root != InvalidHandle)
                        {
                            byte* buffer = stackalloc byte[NtConstants.RenameStructSize + relative.Length * 2];
                            buffer[0] = *(byte*)(info + 0);
                            *(nint*)(buffer + 8) = root;
                            *(uint*)(buffer + 16) = (uint)(relative.Length * 2);
                            relative.AsSpan().CopyTo(new Span<char>((char*)(buffer + NtConstants.RenameFileNameOffset), relative.Length));

                            return ((delegate* unmanaged<nint, IoStatusBlock*, void*, uint, FileInformationClass, int>)_setInformation)(
                                handle, io, buffer, (uint)(NtConstants.RenameStructSize + relative.Length * 2), fileClass);
                        }
                    }
                }
            }
        }

        return ((delegate* unmanaged<nint, IoStatusBlock*, void*, uint, FileInformationClass, int>)_setInformation)(
            handle, io, information, length, fileClass);
    }

    [UnmanagedCallersOnly]
    private static int OnDeleteFile(ObjectAttributes* attributes)
    {
        ObjectAttributes patched = default;
        UnicodeString name = default;
        char* buffer = stackalloc char[BufferChars];

        bool redirected = TryRedirect(attributes, &patched, &name, buffer, "NtDeleteFile", out _);
        return ((delegate* unmanaged<ObjectAttributes*, int>)_deleteFile)(redirected ? &patched : attributes);
    }

    [UnmanagedCallersOnly]
    private static int OnQueryDirectory(nint handle, nint eventHandle, void* apcRoutine, void* apcContext,
        IoStatusBlock* io, void* fileInformation, uint length, FileInformationClass fileClass, byte singleEntry,
        UnicodeString* fileName, byte restartScan)
    {
        if (fileName is not null && fileName->Buffer is not null)
            _ = new string(fileName->Buffer, 0, fileName->Length / 2);

        return ((delegate* unmanaged<nint, nint, void*, void*, IoStatusBlock*, void*, uint, FileInformationClass, byte, UnicodeString*, byte, int>)_queryDirectory)(
            handle, eventHandle, apcRoutine, apcContext, io, fileInformation, length, fileClass, singleEntry, fileName, restartScan);
    }

    [UnmanagedCallersOnly]
    private static int OnCreateSection(nint* sectionHandle, uint access, ObjectAttributes* attributes,
        long* maximumSize, uint protection, uint allocationAttributes, nint fileHandle)
    {
        if (attributes is not null && attributes->ObjectName is not null && attributes->ObjectName->Buffer is not null)
        {
            string original = new(attributes->ObjectName->Buffer, 0, attributes->ObjectName->Length / 2);
            string relative = PathRedirector.GetRedirectedRelativePath(original);
            if (relative.Length > 0)
            {
                nint root = PathRedirector.GetRootHandle(_config!);
                if (root != InvalidHandle)
                {
                    char* buffer = stackalloc char[relative.Length + 1];
                    relative.AsSpan().CopyTo(new Span<char>(buffer, relative.Length));
                    buffer[relative.Length] = '\0';

                    var name = new UnicodeString
                    {
                        Length = (ushort)(relative.Length * 2),
                        MaximumLength = (ushort)((relative.Length + 1) * 2),
                        Buffer = buffer,
                    };

                    ObjectAttributes redirected = *attributes;
                    redirected.ObjectName = &name;
                    redirected.RootDirectory = root;

                    return ((delegate* unmanaged<nint*, uint, ObjectAttributes*, long*, uint, uint, nint, int>)_createSection)(
                        sectionHandle, access, &redirected, maximumSize, protection, allocationAttributes, fileHandle);
                }
            }
        }

        return ((delegate* unmanaged<nint*, uint, ObjectAttributes*, long*, uint, uint, nint, int>)_createSection)(
            sectionHandle, access, attributes, maximumSize, protection, allocationAttributes, fileHandle);
    }
}
