using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Portal.Bedrock.Hook;

internal static class XUserBridge
{
	internal static class NativeMethods
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		internal static extern nint GetModuleHandleW(string moduleName);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
		internal static extern nint LoadLibraryExW(string fileName, nint file, uint flags);

		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
		internal static extern nint GetProcAddress(nint module, string name);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
		internal static extern uint GetModuleFileNameW(nint module, char[] fileName, uint size);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
		internal static extern uint GetSystemDirectoryW(char[] buffer, uint size);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		internal static extern void OutputDebugStringW(string output);
	}

	private static readonly object SessionLock = new object();

	private static XUserSession? _session;

	private static nint _originalQueryApi;

	private static bool _hookInstalled;

	private static readonly byte[] IdentityAnsiStorage = new byte[1] { 65 };

	private static readonly GCHandle IdentityAnsiPin = GCHandle.Alloc(IdentityAnsiStorage, GCHandleType.Pinned);

	private static readonly byte[] IdentityUtf16Storage = new byte[1] { 87 };

	private static readonly GCHandle IdentityUtf16Pin = GCHandle.Alloc(IdentityUtf16Storage, GCHandleType.Pinned);

	private static readonly byte[] IdentityAddStorage = new byte[1];

	private static readonly GCHandle IdentityAddPin = GCHandle.Alloc(IdentityAddStorage, GCHandleType.Pinned);

	public static nint IdentityAnsi => IdentityAnsiPin.AddrOfPinnedObject();

	public static nint IdentityUtf16 => IdentityUtf16Pin.AddrOfPinnedObject();

	public static nint IdentityAdd => IdentityAddPin.AddrOfPinnedObject();

	public static Action<string, bool>? LogSink { get; set; }

	public static XUserSession? Session
	{
		get
		{
			lock (SessionLock)
			{
				return _session;
			}
		}
	}

	public static void Initialize()
	{
		Info("XUser Bridge 入口已执行 | protocol=1 | mode=pipe-gated | hook=QueryApiImpl-only");
		if (_hookInstalled)
		{
			Warn("XUser Bridge 已存在活动会话；跳过重复初始化");
			return;
		}
		byte[] array;
		try
		{
			array = XUserPipeClient.ReceiveSessionPayload();
		}
		catch (Exception ex)
		{
			Warn("BMCBL 安全会话读取失败；不安装 QueryApiImpl Hook；继续使用微软官方 XUser 登录 | reason=" + ex.Message);
			return;
		}
		if (array == null)
		{
			Info("未检测到 BMCBL 安全一次性管道；不安装 QueryApiImpl Hook；继续使用官方 XUser 登录");
			return;
		}
		XUserSession xUserSession;
		try
		{
			xUserSession = XUserSession.ParseSession(array);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(array);
		}
		if (xUserSession == null)
		{
			Warn("BMCBL 安全会话验证失败；不安装 QueryApiImpl Hook；继续使用微软官方 XUser 登录");
			return;
		}
		Info("已从 BMCBL 安全一次性管道接收并验证 Xbox 会话 | xbox_gamertag=" + SanitizeGamertag(xUserSession.Gamertag) + " | next=load-official-runtime-and-hook");
		if (!InstallHook(xUserSession))
		{
			Error("QueryApiImpl Hook 安装失败；自定义 XUser 已停用；继续使用微软官方 XUser 登录");
		}
	}

	private unsafe static bool InstallHook(XUserSession session)
	{
		nint num = NativeMethods.GetModuleHandleW("xgameruntime.dll");
		string text = "host-preloaded";
		if (num == 0)
		{
			Info("已认证 BMCBL 会话，但系统原生 xgameruntime.dll 尚未映射；正在从 System32 同步加载官方 Runtime");
			num = NativeMethods.LoadLibraryExW("xgameruntime.dll", 0, 2048u);
			if (num == 0)
			{
				Error("failed to load official xgameruntime.dll from System32");
				return false;
			}
			text = "bloader-system32-preload";
		}
		string text2 = ModulePath(num);
		if (text2 == null || !VerifySystemRuntimePath(text2))
		{
			Error("refusing to hook a non-System32 xgameruntime.dll | path=" + (text2 ?? "<unavailable>"));
			return false;
		}
		Info("系统原生 xgameruntime.dll 已就绪 | source=" + text + " | path=" + text2);
		nint procAddress = NativeMethods.GetProcAddress(num, "QueryApiImpl");
		if (procAddress == 0)
		{
			Error("official xgameruntime.dll does not export QueryApiImpl");
			return false;
		}
		Info($"已定位系统原生 QueryApiImpl | address=0x{procAddress:X}");
		if (!InlineHook.TryCreate(procAddress, (nint)(delegate* unmanaged<Guid*, Guid*, void**, int>)(&QueryApiHook), out var trampoline))
		{
			Error("QueryApiImpl Hook 创建失败");
			return false;
		}
		lock (SessionLock)
		{
			if (_hookInstalled)
			{
				return false;
			}
			_originalQueryApi = trampoline;
			_session = session;
			_hookInstalled = true;
		}
		Info($"XUser Bridge 已启用；仅接管官方 QueryApiImpl | xbox_gamertag={SanitizeGamertag(session.Gamertag)} | native_runtime_source={text} | QueryApiImpl=0x{procAddress:X} | trampoline=0x{trampoline:X}");
		return true;
	}

	public unsafe static int CallOriginalQuery(Guid* runtimeClassId, Guid* interfaceId, void** outPtr)
	{
		if (_originalQueryApi == 0)
		{
			return -2147467259;
		}
		return ((delegate* unmanaged<Guid*, Guid*, void**, int>)_originalQueryApi)(runtimeClassId, interfaceId, outPtr);
	}

	[UnmanagedCallersOnly(EntryPoint = "XUserQueryApiHook")]
	private unsafe static int QueryApiHook(Guid* runtimeClassId, Guid* interfaceId, void** outPtr)
	{
		if (runtimeClassId == null || interfaceId == null || outPtr == null)
		{
			return -2147467261;
		}
		*outPtr = null;
		if (*runtimeClassId == XUserAbi.ClsidXUserImpl)
		{
			string text = ((Session != null) ? SanitizeGamertag(Session.Gamertag) : "<unknown>");
			Info("QueryApiImpl 已请求 CLSID_XUserImpl；返回 Portal 内置 XUser | xbox_gamertag=" + text + " | iid=" + (*interfaceId));
			return XUserObject.QueryInterface(interfaceId, outPtr);
		}
		Info("QueryApiImpl 转发官方 | clsid=" + (*runtimeClassId) + " iid=" + (*interfaceId));
		return CallOriginalQuery(runtimeClassId, interfaceId, outPtr);
	}

	private static string? ModulePath(nint module)
	{
		char[] array = new char[32768];
		uint moduleFileNameW = NativeMethods.GetModuleFileNameW(module, array, (uint)array.Length);
		if (moduleFileNameW == 0 || moduleFileNameW >= array.Length)
		{
			return null;
		}
		return new string(array, 0, (int)moduleFileNameW);
	}

	private static bool VerifySystemRuntimePath(string actual)
	{
		char[] array = new char[32768];
		uint systemDirectoryW = NativeMethods.GetSystemDirectoryW(array, (uint)array.Length);
		if (systemDirectoryW == 0 || systemDirectoryW >= array.Length)
		{
			return false;
		}
		string text = Path.Combine(new string(array, 0, (int)systemDirectoryW), "xgameruntime.dll");
		return string.Equals(actual.TrimStart(new char[3] { '\\', '?', '\\' }), text.TrimStart(new char[3] { '\\', '?', '\\' }), StringComparison.OrdinalIgnoreCase);
	}

	private static string SanitizeGamertag(string value)
	{
		StringBuilder stringBuilder = new StringBuilder(64);
		foreach (char c in value)
		{
			if (!char.IsControl(c))
			{
				stringBuilder.Append(c);
			}
			if (stringBuilder.Length >= 64)
			{
				break;
			}
		}
		string text = stringBuilder.ToString().Trim();
		if (text.Length != 0)
		{
			return text;
		}
		return "<unknown>";
	}

	public static void Info(string message)
	{
		NativeMethods.OutputDebugStringW("[Portal XUser] " + message);
		LogSink?.Invoke(message, arg2: true);
	}

	public static void Warn(string message)
	{
		NativeMethods.OutputDebugStringW("[Portal XUser] " + message);
		LogSink?.Invoke(message, arg2: false);
	}

	public static void Error(string message)
	{
		NativeMethods.OutputDebugStringW("[Portal XUser] " + message);
		LogSink?.Invoke(message, arg2: false);
	}
}
