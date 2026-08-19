using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Portal.Localization;

namespace Portal.Bedrock.Xbox;

public sealed class SuspendedProcess : IDisposable
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct StartupInfo
	{
		internal uint Cb;

		internal unsafe char* Reserved;

		internal unsafe char* Desktop;

		internal unsafe char* Title;

		internal uint X;

		internal uint Y;

		internal uint XSize;

		internal uint YSize;

		internal uint XCountChars;

		internal uint YCountChars;

		internal uint FillAttribute;

		internal uint Flags;

		internal ushort ShowWindow;

		internal ushort Reserved2;

		internal unsafe byte* Reserved2Pointer;

		internal nint StandardInput;

		internal nint StandardOutput;

		internal nint StandardError;
	}

	private struct ProcessInformation
	{
		internal nint Process;

		internal nint Thread;

		internal uint ProcessId;

		internal uint ThreadId;
	}

	private const uint CreateSuspended = 4u;

	private const uint CreateNewConsole = 16u;

	private nint _process;

	private nint _thread;

	private bool _resumed;

	public uint ProcessId { get; }

	private SuspendedProcess(ProcessInformation information)
	{
		_process = information.Process;
		_thread = information.Thread;
		ProcessId = information.ProcessId;
	}

	public unsafe static SuspendedProcess Start(string executable, string? arguments = null, string? workingDirectory = null, bool newConsole = false)
	{
		string fullPath = Path.GetFullPath(executable);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException(string.Format(CommonLanguageManager.Instance.bedrockAuth_targetNotFound.CurrentValue(), fullPath), fullPath);
		}
		string fullPath2 = Path.GetFullPath(workingDirectory ?? Path.GetDirectoryName(fullPath)!);
		char[] array = string.Concat(Quote(fullPath) + (string.IsNullOrWhiteSpace(arguments) ? string.Empty : (" " + arguments)), "\0").ToCharArray();
		StartupInfo startupInfo = new StartupInfo
		{
			Cb = (uint)sizeof(StartupInfo)
		};
		uint creationFlags = (uint)(CreateSuspended | (newConsole ? CreateNewConsole : 0));
		ProcessInformation information = default(ProcessInformation);
		fixed (char* commandLine = array)
		{
			if (CreateProcessW(fullPath, commandLine, 0, 0, 0, creationFlags, 0, fullPath2, &startupInfo, &information) == 0)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError(), CommonLanguageManager.Instance.bedrockAuth_createProcessFailed.CurrentValue());
			}
		}
		return new SuspendedProcess(information);
	}

	public void Resume()
	{
		if (!_resumed)
		{
			if (ResumeThread(_thread) == uint.MaxValue)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError(), CommonLanguageManager.Instance.bedrockAuth_resumeThreadFailed.CurrentValue());
			}
			_resumed = true;
		}
	}

	public void Terminate(uint exitCode = 1u)
	{
		if (_process != 0)
		{
			TerminateProcess(_process, exitCode);
		}
	}

	private static string Quote(string value)
	{
		return "\"" + value.Replace("\"", "\\\"") + "\"";
	}

	public void Dispose()
	{
		nint num = Interlocked.Exchange(ref _thread, 0);
		if (num != 0)
		{
			CloseHandle(num);
		}
		nint num2 = Interlocked.Exchange(ref _process, 0);
		if (num2 != 0)
		{
			CloseHandle(num2);
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
	private unsafe static extern int CreateProcessW(string applicationName, char* commandLine, nint processAttributes, nint threadAttributes, int inheritHandles, uint creationFlags, nint environment, string currentDirectory, StartupInfo* startupInfo, ProcessInformation* processInformation);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern uint ResumeThread(nint thread);

	[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
	private static extern int TerminateProcess(nint process, uint exitCode);

	[DllImport("kernel32.dll", ExactSpelling = true)]
	private static extern int CloseHandle(nint handle);
}
