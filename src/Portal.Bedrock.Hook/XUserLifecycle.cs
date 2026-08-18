using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Portal.Bedrock.Hook;

internal static class XUserLifecycle
{
	private sealed class ChangeRegistration
	{
		public ulong Token;

		public nint Context;

		public unsafe delegate* unmanaged<void*, XUserLocalId, uint, void> Callback;

		public bool Active;
	}

	private const uint XUserChangeEventSignedInAgain = 0u;

	private const uint XUserChangeEventSigningOut = 1u;

	private const uint XUserChangeEventSignedOut = 2u;

	private const ulong SignOutDeferralMarker = 4777280446470115925uL;

	private const int ProcessSignOutWaitMs = 2000;

	private static readonly object Lock = new object();

	private static uint _userState = 0u;

	private static int _userHandleCount;

	private static bool _userWasAdded;

	private static bool _signoutCallbacksComplete = true;

	private static int _signoutDeferrals;

	private static ulong _nextRegistrationToken = 1uL;

	private static readonly Dictionary<ulong, ChangeRegistration> Registrations = new Dictionary<ulong, ChangeRegistration>();

	private static bool _processShutdownStarted;

	private static nint _originalRtlExitUserProcess;

	private static bool _exitHookInstalled;

	public static uint State
	{
		get
		{
			lock (Lock)
			{
				return _userState;
			}
		}
	}

	public static bool ActiveHandleExists
	{
		get
		{
			lock (Lock)
			{
				return _userState == 0;
			}
		}
	}

	public static int AcquireAddedHandle()
	{
		lock (Lock)
		{
			switch (_userState)
			{
			case 0u:
				_userWasAdded = true;
				_userHandleCount++;
				return 0;
			case 2u:
				if (_processShutdownStarted)
				{
					return -1994108671;
				}
				_userState = 0u;
				_userWasAdded = true;
				_userHandleCount = 1;
				_signoutDeferrals = 0;
				_signoutCallbacksComplete = true;
				XUserBridge.Info("XUser 生命周期已恢复为 SignedIn；正在发送 SignedInAgain 事件");
				DispatchChangeEvent(XUserChangeEventSignedInAgain);
				return 0;
			case 1u:
				return -1994108671;
			default:
				return -2147467259;
			}
		}
	}

	public static int DuplicateActiveHandle()
	{
		lock (Lock)
		{
			if (_userState != 0)
			{
				return -1994108671;
			}
			_userHandleCount++;
			return 0;
		}
	}

	public static void ReleaseUserHandle()
	{
		lock (Lock)
		{
			if (_userHandleCount > 0)
			{
				_userHandleCount--;
			}
		}
	}

	public unsafe static int RegisterForChangeEvent(nint context, delegate* unmanaged<void*, XUserLocalId, uint, void> callback, XTaskQueueRegistrationToken* token)
	{
		if (token == null || callback == (delegate* unmanaged<void*, XUserLocalId, uint, void>)null)
		{
			return -2147467261;
		}
		EnsureProcessExitHook();
		bool flag = false;
		lock (Lock)
		{
			if (_userState == 2 && !_processShutdownStarted)
			{
				_userState = 0u;
				_signoutDeferrals = 0;
				_signoutCallbacksComplete = true;
				flag = true;
				XUserBridge.Info("XUser 变更订阅重新建立；生命周期恢复为 SignedIn");
			}
			ulong num = _nextRegistrationToken++;
			if (num == 0L)
			{
				num = _nextRegistrationToken++;
			}
			Registrations[num] = new ChangeRegistration
			{
				Token = num,
				Context = context,
				Callback = callback,
				Active = true
			};
			token->Token = num;
		}
		if (flag)
		{
			DispatchChangeEvent(XUserChangeEventSignedInAgain);
		}
		return 0;
	}

	public static byte UnregisterForChangeEvent(XTaskQueueRegistrationToken token, byte wait)
	{
		if (token.Token == 0L)
		{
			return 1;
		}
		lock (Lock)
		{
			if (!Registrations.Remove(token.Token, out ChangeRegistration _))
			{
				return 1;
			}
			int num = 0;
			foreach (ChangeRegistration value2 in Registrations.Values)
			{
				if (value2.Active)
				{
					num++;
				}
			}
			if (num == 0 && _userState == 0 && _userWasAdded && !_processShutdownStarted)
			{
				XUserBridge.Info("最后一个 XUser 变更订阅正在注销；在移除回调前派发 SigningOut 生命周期");
				BeginSignOutLocked("last-change-registration-unregister");
			}
			return 1;
		}
	}

	public unsafe static int GetSignOutDeferral(nint* deferral)
	{
		if (deferral == null)
		{
			return -2147467261;
		}
		*deferral = 0;
		lock (Lock)
		{
			if (_userState != 1)
			{
				return -1994108669;
			}
			_signoutDeferrals++;
			nint num = Marshal.AllocHGlobal(8);
			*(long*)num = (long)SignOutDeferralMarker;
			*deferral = num;
			return 0;
		}
	}

	public unsafe static void CloseSignOutDeferralHandle(nint deferral)
	{
		if (deferral == 0)
		{
			return;
		}
		ulong num;
		lock (Lock)
		{
			num = *(ulong*)deferral;
		}
		if (num != 4777280446470115925L)
		{
			XUserBridge.Warn("XUserCloseSignOutDeferralHandle 收到无效句柄");
			return;
		}
		Marshal.FreeHGlobal(deferral);
		lock (Lock)
		{
			if (_signoutDeferrals > 0)
			{
				_signoutDeferrals--;
			}
			TryCompleteSignOutLocked();
		}
	}

	private unsafe static void DispatchChangeEvent(uint eventId)
	{
		if (XUserBridge.Session == null)
		{
			return;
		}
		ChangeRegistration[] array;
		lock (Lock)
		{
			array = Registrations.Values.Where((ChangeRegistration r) => r.Active).ToArray();
		}
		ulong localId = XUserBridge.Session.LocalId;
		ChangeRegistration[] array2 = array;
		foreach (ChangeRegistration changeRegistration in array2)
		{
			changeRegistration.Callback((void*)changeRegistration.Context, new XUserLocalId
			{
				Value = localId
			}, eventId);
		}
	}

	private static void BeginSignOutLocked(string trigger)
	{
		if (_userWasAdded && _userState == 0)
		{
			_userState = 1u;
			_signoutCallbacksComplete = false;
			XUserBridge.Info($"XUser 状态进入 SigningOut | trigger={trigger} | diagnostic_handles={_userHandleCount}");
			DispatchChangeEvent(XUserChangeEventSigningOut);
			_signoutCallbacksComplete = true;
			TryCompleteSignOutLocked();
		}
	}

	private static void TryCompleteSignOutLocked()
	{
		if (_signoutCallbacksComplete && _signoutDeferrals == 0 && _userState == 1)
		{
			_userState = 2u;
			XUserBridge.Info("XUser 注销回调与延迟已完成；XUser 状态进入 SignedOut");
			DispatchChangeEvent(XUserChangeEventSignedOut);
		}
	}

	private static void BeginProcessShutdown(int exitStatus)
	{
		XUserBridge.Info($"检测到正常进程退出；正在提交 XUser 注销生命周期 | source=ntdll!RtlExitUserProcess | exit_status=0x{exitStatus & 0xFFFFFFFFu:X8}");
		if (!_userWasAdded)
		{
			_userState = 2u;
			XUserBridge.Info("进程退出时没有已添加的 XUser；无需派发注销回调");
			return;
		}
		lock (Lock)
		{
			BeginSignOutLocked("process-exit");
		}
		long num = Environment.TickCount64 + ProcessSignOutWaitMs;
		while (true)
		{
			lock (Lock)
			{
				if (_userState == 2)
				{
					return;
				}
			}
			if (Environment.TickCount64 >= num)
			{
				break;
			}
			Thread.Sleep(5);
		}
		lock (Lock)
		{
			if (_userState != 2)
			{
				_userState = 2u;
				XUserBridge.Warn("XUser 注销等待超时；进程即将退出，强制进入 SignedOut");
				DispatchChangeEvent(XUserChangeEventSignedOut);
			}
		}
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserRtlExitUserProcessHook")]
	private unsafe static void RtlExitUserProcessHook(int exitStatus)
	{
		lock (Lock)
		{
			if (!_processShutdownStarted)
			{
				_processShutdownStarted = true;
				BeginProcessShutdown(exitStatus);
			}
		}
		if (_originalRtlExitUserProcess != 0)
		{
			((delegate* unmanaged<int, void>)_originalRtlExitUserProcess)(exitStatus);
		}
	}

	private unsafe static void EnsureProcessExitHook()
	{
		if (_exitHookInstalled)
		{
			return;
		}
		lock (Lock)
		{
			if (_exitHookInstalled)
			{
				return;
			}
			nint moduleHandleW = XUserBridge.NativeMethods.GetModuleHandleW("ntdll.dll");
			if (moduleHandleW != 0)
			{
				nint procAddress = XUserBridge.NativeMethods.GetProcAddress(moduleHandleW, "RtlExitUserProcess");
				if (procAddress != 0 && InlineHook.TryCreate(procAddress, (nint)(delegate* unmanaged<int, void>)(&RtlExitUserProcessHook), out var trampoline))
				{
					_originalRtlExitUserProcess = trampoline;
					_exitHookInstalled = true;
				}
			}
		}
	}
}
