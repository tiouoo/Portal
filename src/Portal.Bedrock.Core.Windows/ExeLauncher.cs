using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Portal.Bedrock.Core.Windows;

public static class ExeLauncher
{
	private struct ProcessInformation
	{
		public nint hProcess;

		public nint hThread;

		public int dwProcessId;

		public int dwThreadId;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct StartupInfo
	{
		public int cb;

		public string? lpReserved;

		public string? lpDesktop;

		public string? lpTitle;

		public int dwX;

		public int dwY;

		public int dwXSize;

		public int dwYSize;

		public int dwXCountChars;

		public int dwYCountChars;

		public int dwFillAttribute;

		public int dwFlags;

		public short wShowWindow;

		public short cbReserved2;

		public nint lpReserved2;

		public nint hStdInput;

		public nint hStdOutput;

		public nint hStdError;
	}

	private enum LogonFlags
	{
		LogonWithProfile = 1,
		LogonNetCredentialsOnly
	}

	private enum CreationFlags
	{
		NormalPriorityClass = 32,
		CreateNoWindow = 134217728,
		CreateUnicodeEnvironment = 1024
	}

	private enum TokenType
	{
		TokenPrimary = 1,
		TokenImpersonation
	}

	private enum SecurityImpersonationLevel
	{
		SecurityAnonymous,
		SecurityIdentification,
		SecurityImpersonation,
		SecurityDelegation
	}

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateProcessWithTokenW(nint hToken, LogonFlags dwLogonFlags, string? lpApplicationName, string lpCommandLine, CreationFlags dwCreationFlags, nint lpEnvironment, string? lpCurrentDirectory, ref StartupInfo lpStartupInfo, out ProcessInformation lpProcessInformation);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool DuplicateTokenEx(nint hExistingToken, uint dwDesiredAccess, nint lpTokenAttributes, SecurityImpersonationLevel ImpersonationLevel, TokenType TokenType, out nint phNewToken);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint hObject);

	public static Process? LaunchWithLowPrivilege(string exePath, string arguments = "")
	{
		nint phNewToken = IntPtr.Zero;
		ProcessInformation lpProcessInformation = default(ProcessInformation);
		try
		{
			if (!File.Exists(exePath))
			{
				throw new FileNotFoundException(" " + exePath);
			}
			using (WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent())
			{
				if (!DuplicateTokenEx(windowsIdentity.Token, 33554432u, IntPtr.Zero, SecurityImpersonationLevel.SecurityIdentification, TokenType.TokenPrimary, out phNewToken))
				{
					throw new Exception($"can't copy token: {Marshal.GetLastWin32Error()}");
				}
			}
			StartupInfo lpStartupInfo = default(StartupInfo);
			lpStartupInfo.cb = Marshal.SizeOf(lpStartupInfo);
			lpStartupInfo.lpDesktop = "Winsta0\\Default";
			lpStartupInfo.dwFlags = 1;
			lpStartupInfo.wShowWindow = 1;
			string lpCommandLine = "\"" + exePath + "\" " + arguments;
			string directoryName = Path.GetDirectoryName(exePath);
			if (!CreateProcessWithTokenW(phNewToken, LogonFlags.LogonWithProfile, null, lpCommandLine, (CreationFlags)134218784, IntPtr.Zero, directoryName, ref lpStartupInfo, out lpProcessInformation))
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				throw new Exception($"failed to start {lastWin32Error}");
			}
			Process processById = Process.GetProcessById(lpProcessInformation.dwProcessId);
			if (lpProcessInformation.hProcess != IntPtr.Zero)
			{
				CloseHandle(lpProcessInformation.hProcess);
			}
			if (lpProcessInformation.hThread != IntPtr.Zero)
			{
				CloseHandle(lpProcessInformation.hThread);
			}
			return processById;
		}
		catch
		{
			return null;
		}
		finally
		{
			if (phNewToken != IntPtr.Zero)
			{
				CloseHandle(phNewToken);
			}
		}
	}
}
