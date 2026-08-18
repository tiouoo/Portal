using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Portal.Bedrock.Standard.Interface;

namespace Portal.Bedrock;

/// <summary>
/// NativeAOT 库采用懒初始化：仅当游戏进程首次调用导出函数时才完成运行时与模块初始化。
/// 游戏仅静态导入 <c>Load</c> 但从不调用，因此这里在游戏启动后通过远程线程主动调用
/// <c>Load</c>，触发预加载组件的初始化和文件重定向挂钩。
/// </summary>
internal static class BedrockPreloadTrigger
{
    private const uint Infinite = 0xFFFFFFFF;

    [Flags]
    private enum ProcessAccessFlags : uint
    {
        CreateThread = 0x0002,
        VmOperation = 0x0008,
        VmRead = 0x0010,
        VmWrite = 0x0020,
        QueryLimitedInformation = 0x1000,
    }

    private static readonly string[] CandidateDllNames = { "Portal.Preload.dll" };

    private const uint ProcessAccess = (uint)(ProcessAccessFlags.CreateThread | ProcessAccessFlags.VmOperation |
                                              ProcessAccessFlags.VmRead | ProcessAccessFlags.VmWrite |
                                              ProcessAccessFlags.QueryLimitedInformation);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nint CreateRemoteThread(nint process, nint threadAttributes, nuint stackSize,
            nint startAddress, nint parameter, uint creationFlags, out uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(nint handle, uint milliseconds);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumProcessModulesEx(nint process, nint[] modules, uint size,
            out uint needed, uint filterFlag);

        [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
        public static extern uint GetModuleBaseNameW(nint process, nint module, char[] baseName, uint size);

        [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetModuleFileNameExW(nint process, nint module, char[] fileName, uint size);
    }

    private const int MaxAttempts = 12;
    private const int RetryDelayMs = 250;

    /// <summary>
    /// 远程触发预加载组件的 <c>Load</c> 导出。可能被调用于进程刚恢复的时刻
    /// （加载器尚未完成预加载 DLL 的装载），因此带短暂重试。
    /// </summary>
    public static void Trigger(Process process, Action<string, BedrockLogLevel>? log = null)
    {
        string? lastWarning = null;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                if (process.HasExited)
                {
                    lastWarning = $"游戏进程已退出（{process.Id}），跳过预加载触发";
                    break;
                }

                nint processHandle = NativeMethods.OpenProcess(ProcessAccess, inheritHandle: false, process.Id);
                if (processHandle == 0)
                {
                    lastWarning = $"打开游戏进程失败（{Marshal.GetLastWin32Error()}），跳过预加载触发";
                    break;
                }

                try
                {
                    foreach (string dllName in CandidateDllNames)
                    {
                        nint moduleBase = FindModuleBase(processHandle, dllName);
                        if (moduleBase == 0)
                            continue;

                        string dllPath = GetModulePath(processHandle, moduleBase);
                        if (string.IsNullOrEmpty(dllPath))
                            continue;

                        nint loadRva = ReadLoadExportRva(dllPath);
                        if (loadRva == 0)
                        {
                            lastWarning = $"未能解析 {dllName} 的 Load 导出，跳过预加载触发";
                            break;
                        }

                        if (CallRemote(processHandle, moduleBase + loadRva))
                        {
                            log?.Invoke($"已触发预加载组件初始化（{dllName} @ 0x{moduleBase:X}）", BedrockLogLevel.Information);
                            return;
                        }

                        lastWarning = $"远程调用 Load 失败（{Marshal.GetLastWin32Error()}）";
                        break;
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(processHandle);
                }
            }
            catch (Exception exception)
            {
                lastWarning = $"触发预加载初始化失败：{exception.Message}";
            }

            if (attempt + 1 < MaxAttempts)
                Thread.Sleep(RetryDelayMs);
        }

        if (lastWarning != null)
        {
            log?.Invoke(lastWarning, BedrockLogLevel.Warning);
            Trace.TraceWarning(lastWarning);
        }
    }

    private static string GetModulePath(nint processHandle, nint module)
    {
        var buffer = new char[1024];
        uint length = NativeMethods.GetModuleFileNameExW(processHandle, module, buffer, (uint)buffer.Length);
        return length == 0 ? string.Empty : new string(buffer, 0, (int)length);
    }

    private static bool CallRemote(nint processHandle, nint functionAddress)
    {
        nint thread = NativeMethods.CreateRemoteThread(processHandle, nint.Zero, 0, functionAddress, nint.Zero, 0, out _);
        if (thread == 0)
            return false;

        NativeMethods.WaitForSingleObject(thread, Infinite);
        NativeMethods.CloseHandle(thread);
        return true;
    }

    private static nint FindModuleBase(nint processHandle, string moduleName)
    {
        for (int capacity = 64; capacity <= 4096; capacity *= 2)
        {
            var modules = new nint[capacity];
            if (!NativeMethods.EnumProcessModulesEx(processHandle, modules, (uint)(capacity * nint.Size),
                    out uint needed, 0))
                return 0;

            if (needed > (uint)(capacity * nint.Size))
                continue;

            int count = (int)(needed / (uint)nint.Size);
            for (int i = 0; i < count; i++)
            {
                string name = GetModuleName(processHandle, modules[i]);
                if (name.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    return modules[i];
            }
            break;
        }

        return 0;
    }

    private static string GetModuleName(nint processHandle, nint module)
    {
        var buffer = new char[260];
        uint length = NativeMethods.GetModuleBaseNameW(processHandle, module, buffer, (uint)buffer.Length);
        return length == 0 ? string.Empty : new string(buffer, 0, (int)length);
    }

    /// <summary>解析 PE 导出表中 <c>Load</c> 函数的 RVA。</summary>
    private static nint ReadLoadExportRva(string dllPath)
    {
        byte[] image = File.ReadAllBytes(dllPath);
        int peOffset = BitConverter.ToInt32(image, 0x3C);
        if (peOffset <= 0 || peOffset + 0x40 >= image.Length)
            return 0;

        int optionalOffset = peOffset + 24;
        ushort magic = BitConverter.ToUInt16(image, optionalOffset);
        bool pe32Plus = magic == 0x20B;
        if (magic is not (0x10B or 0x20B))
            return 0;

        int dataDirectoryOffset = optionalOffset + (pe32Plus ? 112 : 96);
        uint exportRva = BitConverter.ToUInt32(image, dataDirectoryOffset);
        if (exportRva == 0)
            return 0;

        int sectionCount = BitConverter.ToUInt16(image, peOffset + 6);
        int sectionTableOffset = optionalOffset + (pe32Plus ? 240 : 224);
        long exportOffset = RvaToFileOffset(image, sectionTableOffset, sectionCount, exportRva);
        if (exportOffset < 0 || exportOffset + 40 > image.Length)
            return 0;

        uint numberOfNames = BitConverter.ToUInt32(image, (int)exportOffset + 24);
        uint addressOfFunctions = BitConverter.ToUInt32(image, (int)exportOffset + 28);
        uint addressOfNames = BitConverter.ToUInt32(image, (int)exportOffset + 32);
        uint addressOfNameOrdinals = BitConverter.ToUInt32(image, (int)exportOffset + 36);

        for (uint i = 0; i < numberOfNames; i++)
        {
            long nameRvaOffset = RvaToFileOffset(image, sectionTableOffset, sectionCount, addressOfNames + i * 4);
            if (nameRvaOffset < 0 || nameRvaOffset + 4 > image.Length)
                return 0;

            uint nameRva = BitConverter.ToUInt32(image, (int)nameRvaOffset);
            long nameOffset = RvaToFileOffset(image, sectionTableOffset, sectionCount, nameRva);
            if (nameOffset < 0 || nameOffset >= image.Length)
                return 0;

            if (!ReadAsciiEquals(image, nameOffset, "Load"))
                continue;

            long ordinalOffset = RvaToFileOffset(image, sectionTableOffset, sectionCount, addressOfNameOrdinals + i * 2);
            if (ordinalOffset < 0 || ordinalOffset + 2 > image.Length)
                return 0;

            ushort ordinal = BitConverter.ToUInt16(image, (int)ordinalOffset);
            long functionOffset = RvaToFileOffset(image, sectionTableOffset, sectionCount, addressOfFunctions + (uint)ordinal * 4);
            if (functionOffset < 0 || functionOffset + 4 > image.Length)
                return 0;

            return (nint)BitConverter.ToUInt32(image, (int)functionOffset);
        }

        return 0;
    }

    private static long RvaToFileOffset(byte[] image, int sectionTableOffset, int sectionCount, uint rva)
    {
        for (int i = 0; i < sectionCount; i++)
        {
            int sectionOffset = sectionTableOffset + i * 40;
            if (sectionOffset + 40 > image.Length)
                return -1;

            uint virtualSize = Math.Max(BitConverter.ToUInt32(image, sectionOffset + 8),
                BitConverter.ToUInt32(image, sectionOffset + 16));
            uint virtualAddress = BitConverter.ToUInt32(image, sectionOffset + 12);
            uint sizeOfRawData = BitConverter.ToUInt32(image, sectionOffset + 16);
            uint pointerToRawData = BitConverter.ToUInt32(image, sectionOffset + 20);

            if (rva >= virtualAddress && rva < virtualAddress + virtualSize && rva - virtualAddress < sizeOfRawData)
                return pointerToRawData + (rva - virtualAddress);
        }

        return -1;
    }

    private static bool ReadAsciiEquals(byte[] image, long offset, string expected)
    {
        int length = expected.Length;
        if (offset < 0 || offset + length >= image.Length)
            return false;

        for (int i = 0; i < length; i++)
        {
            if (image[offset + i] != (byte)expected[i])
                return false;
        }
        return image[offset + length] == 0;
    }
}
