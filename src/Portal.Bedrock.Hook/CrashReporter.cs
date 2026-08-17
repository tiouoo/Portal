using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Portal.Bedrock.Hook;

internal static class CrashReporter
{
	private struct MemoryBasicInformation
	{
		public nint BaseAddress;

		public nint AllocationBase;

		public uint AllocationProtect;

		public uint RegionSize;

		public uint State;

		public uint Protect;

		public uint Type;
	}

	private struct MiniDumpExceptionInformation
	{
		public uint ThreadId;

		public nint ExceptionPointers;

		public int ClientPointers;
	}

	private static class NativeMethods
	{
		[DllImport("kernel32.dll", ExactSpelling = true)]
		internal unsafe static extern nint AddVectoredExceptionHandler(int first, delegate* unmanaged<nint, long> handler);

		[DllImport("kernel32.dll", ExactSpelling = true)]
		internal unsafe static extern nint SetUnhandledExceptionFilter(delegate* unmanaged<nint, long> handler);

		[DllImport("kernel32.dll", ExactSpelling = true)]
		internal static extern uint GetCurrentThreadId();

		[DllImport("kernel32.dll", ExactSpelling = true)]
		internal static extern nint GetCurrentProcess();

		[DllImport("kernel32.dll", ExactSpelling = true)]
		internal static extern uint GetCurrentProcessId();

		[DllImport("kernel32.dll", ExactSpelling = true)]
		internal unsafe static extern nuint VirtualQuery(nint address, MemoryBasicInformation* buffer, nuint length);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		internal static extern uint GetModuleFileNameW(nint module, char[] fileName, uint size);

		[DllImport("dbghelp.dll", ExactSpelling = true)]
		internal unsafe static extern int MiniDumpWriteDump(nint process, uint processId, nint file, uint dumpType, MiniDumpExceptionInformation* exceptionParam, nint userStreamParam, nint callbackParam);
	}

	private const uint ExceptionContinueSearch = 0u;

	private static readonly uint[] FatalCodes = new uint[11]
	{
		1073741845u, 2147483651u, 3221225477u, 3221225501u, 3221225509u, 3221225620u, 3221225622u, 3221225725u, 3221226505u, 3221226525u,
		3221227010u
	};

	private static nint _unhandledFilter;

	private static ulong _lastSignature;

	private const uint MiniDumpNormal = 0u;

	private const uint MiniDumpWithIndirectlyReferencedMemory = 128u;

	private const uint MiniDumpWithThreadInfo = 4096u;

	public unsafe static void Install()
	{
		try
		{
			NativeMethods.AddVectoredExceptionHandler(1, (delegate* unmanaged<nint, long>)(&VectoredHandler));
			XUserBridge.Info("CrashReporter 已安装 VEH 首异常处理器");
		}
		catch (Exception ex)
		{
			XUserBridge.Warn("安装 VEH 失败: " + ex.Message);
		}
		try
		{
			_unhandledFilter = NativeMethods.SetUnhandledExceptionFilter((delegate* unmanaged<nint, long>)(&UnhandledFilter));
			XUserBridge.Info("CrashReporter 已安装 SEH 顶层过滤器");
		}
		catch (Exception ex2)
		{
			XUserBridge.Warn("安装 SEH 过滤器失败: " + ex2.Message);
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "CrashVectoredHandler")]
	private static long VectoredHandler(nint pointers)
	{
		if (pointers == 0)
		{
			return 0L;
		}
		uint value = ReadExceptionCode(pointers);
		if (Array.IndexOf(FatalCodes, value) < 0)
		{
			return 0L;
		}
		Capture(pointers, "first-chance");
		return 0L;
	}

	[UnmanagedCallersOnly(EntryPoint = "CrashUnhandledFilter")]
	private static long UnhandledFilter(nint pointers)
	{
		if (pointers != 0)
		{
			Capture(pointers, "unhandled", writeMinidump: true);
		}
		return 0L;
	}

	private unsafe static uint ReadExceptionCode(nint exceptionPointers)
	{
		nint num = *(nint*)exceptionPointers;
		if (num != 0)
		{
			return *(uint*)num;
		}
		return 0u;
	}

	private unsafe static void Capture(nint exceptionPointers, string phase, bool writeMinidump = false)
	{
		try
		{
			nint num = *(nint*)exceptionPointers;
			if (num == 0)
			{
				return;
			}
			uint num2 = *(uint*)num;
			nint num3 = *(nint*)(num + 16);
			uint num4 = *(uint*)(num + 24);
			ulong num5b = (num4 >= 2) ? *(ulong*)(num + 32 + 8) : 0uL;
			uint currentThreadId = NativeMethods.GetCurrentThreadId();
			ulong num5 = (ulong)((long)num3 << 17) ^ ((ulong)num2 << 32) ^ currentThreadId;
			if (num5 == _lastSignature)
			{
				return;
			}
			_lastSignature = num5;
			nint num6 = *(nint*)(exceptionPointers + 8);
			ulong value = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 248)));
			ulong value2 = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 152)));
			ulong rax = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 120)));
			ulong rcx = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 128)));
			ulong rdx = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 136)));
			ulong rbx = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 144)));
			ulong rbp = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 160)));
			ulong rsi = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 168)));
			ulong rdi = ((num6 == 0) ? 0 : (*(ulong*)(num6 + 176)));
			string reportDirectory = GetReportDirectory();
			string value3 = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string value4 = DetermineOwner((nint)value);
			string text = Path.Combine(reportDirectory, $"crash-{phase}-{value4}-{value3}.txt");
			StringBuilder stringBuilder = new StringBuilder(512);
			stringBuilder.AppendLine("Portal Bedrock Preload Crash Report");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
			handler.AppendLiteral("Phase: ");
			handler.AppendFormatted(phase);
			stringBuilder3.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(6, 1, stringBuilder2);
			handler.AppendLiteral("Time: ");
			handler.AppendFormatted(DateTime.Now, "O");
			stringBuilder4.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
			handler.AppendLiteral("Exception Code: 0x");
			handler.AppendFormatted(num2, "X8");
			stringBuilder5.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder51 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(21, 1, stringBuilder2);
			handler.AppendLiteral("Exception Address: 0x");
			handler.AppendFormatted(num3, "X");
			stringBuilder51.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder52 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("Faulting Data: 0x");
			handler.AppendFormatted(num5b, "X");
			stringBuilder52.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder53 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder2);
			handler.AppendLiteral("Thread ID: ");
			handler.AppendFormatted(currentThreadId);
			stringBuilder53.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder54 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("Attribution: ");
			handler.AppendFormatted(value4);
			stringBuilder54.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder55 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 2, stringBuilder2);
			handler.AppendLiteral("Registers: rip=0x");
			handler.AppendFormatted(value, "X");
			handler.AppendLiteral(" rsp=0x");
			handler.AppendFormatted(value2, "X");
			stringBuilder55.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder56 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(62, 7, stringBuilder2);
			handler.AppendLiteral(" rax=0x");
			handler.AppendFormatted(rax, "X");
			handler.AppendLiteral(" rbx=0x");
			handler.AppendFormatted(rbx, "X");
			handler.AppendLiteral(" rcx=0x");
			handler.AppendFormatted(rcx, "X");
			handler.AppendLiteral(" rdx=0x");
			handler.AppendFormatted(rdx, "X");
			handler.AppendLiteral(" rbp=0x");
			handler.AppendFormatted(rbp, "X");
			handler.AppendLiteral(" rsi=0x");
			handler.AppendFormatted(rsi, "X");
			handler.AppendLiteral(" rdi=0x");
			handler.AppendFormatted(rdi, "X");
			stringBuilder56.AppendLine(ref handler);
			if (num4 >= 2)
			{
				nint num7 = *(nint*)(num + 32);
				nint value5 = *(nint*)(num + 40);
				if (num2 == 3221225477u)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder10 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
					handler.AppendLiteral("Access Type: ");
					handler.AppendFormatted((num7 == 1) ? "WRITE" : "READ");
					stringBuilder10.AppendLine(ref handler);
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder11 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
					handler.AppendLiteral("Fault Address: 0x");
					handler.AppendFormatted(value5, "X");
					stringBuilder11.AppendLine(ref handler);
				}
			}
			Directory.CreateDirectory(reportDirectory);
			File.WriteAllText(text, stringBuilder.ToString());
			XUserBridge.Info("崩溃报告已写入: " + text);
			if (writeMinidump)
			{
				string path = Path.Combine(reportDirectory, $"crash-{phase}-{value4}-{value3}.dmp");
				WriteMinidump(exceptionPointers, path);
			}
		}
		catch
		{
		}
	}

	private static string GetReportDirectory()
	{
		string text = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
		InlineArray5<string> buffer = default(InlineArray5<string>);
		buffer[0] = text;
		buffer[1] = "config";
		buffer[2] = "Portal";
		buffer[3] = "logs";
		buffer[4] = "crash-reports";
		return Path.Combine(buffer);
	}

	private unsafe static string DetermineOwner(nint address)
	{
		if (address == 0)
		{
			return "unresolved";
		}
		MemoryBasicInformation memoryBasicInformation = default(MemoryBasicInformation);
		if (NativeMethods.VirtualQuery(address, &memoryBasicInformation, (nuint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
		{
			return "unresolved";
		}
		char[] array = new char[1024];
		uint moduleFileNameW = NativeMethods.GetModuleFileNameW(memoryBasicInformation.AllocationBase, array, (uint)array.Length);
		if (moduleFileNameW == 0)
		{
			return "unresolved";
		}
		string text = new string(array, 0, (int)moduleFileNameW);
		string fileName = Path.GetFileName(text);
		if (text.Contains("\\mods\\", StringComparison.OrdinalIgnoreCase) || text.Contains("\\preload\\", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("bl_", StringComparison.OrdinalIgnoreCase))
		{
			return "mod(" + fileName + ")";
		}
		if (fileName.StartsWith("Portal.Preload", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("XUserHook", StringComparison.OrdinalIgnoreCase))
		{
			return "loader(" + fileName + ")";
		}
		return fileName;
	}

	private unsafe static void WriteMinidump(nint exceptionPointers, string path)
	{
		try
		{
			MiniDumpExceptionInformation miniDumpExceptionInformation = new MiniDumpExceptionInformation
			{
				ThreadId = NativeMethods.GetCurrentThreadId(),
				ExceptionPointers = exceptionPointers,
				ClientPointers = 1
			};
			using FileStream fileStream = File.Create(path);
			XUserBridge.Info((NativeMethods.MiniDumpWriteDump(NativeMethods.GetCurrentProcess(), NativeMethods.GetCurrentProcessId(), fileStream.SafeFileHandle.DangerousGetHandle(), 4224u, &miniDumpExceptionInformation, IntPtr.Zero, IntPtr.Zero) != 0) ? ("minidump 已写入: " + path) : $"minidump 写入失败: {Marshal.GetLastWin32Error()}");
		}
		catch (Exception ex)
		{
			XUserBridge.Warn("minidump 生成失败: " + ex.Message);
		}
	}
}
