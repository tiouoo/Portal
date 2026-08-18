using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Portal.Bedrock.Hook.Mods;

internal static class ModLoader
{
	private sealed class PreloadMod
	{
		public string Id = string.Empty;

		public string Name = string.Empty;

		public string DllPath = string.Empty;

		public bool Required;

		public List<string> VerifyExports = new List<string>();

		public List<string> VerifyModules = new List<string>();

		public bool NotifySuccess;
	}

	private sealed class HotMod
	{
		public string Id = string.Empty;

		public string Name = string.Empty;

		public string DllPath = string.Empty;

		public ulong InjectDelayMs;

		public ReadyLevel ReadyLevel;

		public bool Required;

		public List<string> VerifyExports = new List<string>();

		public List<string> VerifyModules = new List<string>();

		public bool NotifySuccess;
	}

	private sealed class BlMod
	{
		public string Id = string.Empty;

		public string Name = string.Empty;

		public string DllPath = string.Empty;

		public uint ApiVersion = 1u;

		public bool RequiresSymbolPack;

		public List<string> RequiredSymbols = new List<string>();
	}

	private enum ReadyLevel
	{
		Process,
		Window,
		StableWindow
	}

	private sealed class DiscoveredMods
	{
		public readonly List<PreloadMod> Preload = new List<PreloadMod>();

		public readonly List<HotMod> Hot = new List<HotMod>();

		public readonly List<BlMod> Bl = new List<BlMod>();

		public bool IsEmpty
		{
			get
			{
				if (Preload.Count == 0 && Hot.Count == 0)
				{
					return Bl.Count == 0;
				}
				return false;
			}
		}
	}

	internal static class NativeMethods
	{
		internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);

		internal struct NativeRect
		{
			public int Left;

			public int Top;

			public int Right;

			public int Bottom;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
		internal static extern nint LoadLibraryW(string fileName);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
		internal static extern nint GetModuleHandleW(string moduleName);

		[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
		internal static extern nint GetProcAddress(nint module, string name);

		[DllImport("user32.dll", ExactSpelling = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

		[DllImport("user32.dll", ExactSpelling = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

		[DllImport("user32.dll", ExactSpelling = true)]
		internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

		[DllImport("user32.dll", ExactSpelling = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool IsWindowVisible(nint hwnd);

		[DllImport("user32.dll", ExactSpelling = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		internal static extern bool GetClientRect(nint hwnd, out NativeRect rect);

		internal static bool HasClientAreaAtLeast(nint hwnd, int minWidth, int minHeight)
		{
			if (GetClientRect(hwnd, out var rect) && rect.Right - rect.Left >= minWidth)
			{
				return rect.Bottom - rect.Top >= minHeight;
			}
			return false;
		}
	}

	private const string ModTypePreloadNative = "preload-native";

	private const string ModTypeHotNative = "hot-native";

	private const string ModTypeHotInject = "hot-inject";

	private const string ModTypeBl = "BL";

	private const ulong HotInjectDefaultDelayMs = 15000uL;

	private const int HotInjectWaitTimeoutMs = 120000;

	private const int StableWindowMs = 750;

	public static void LoadMods(string gameDir)
	{
		string text = Path.Combine(gameDir, "mods");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
			XUserBridge.Warn("Mods 目录不可写；跳过 mod 装载");
			return;
		}
		DiscoveredMods discoveredMods = Discover(text);
		if (discoveredMods.IsEmpty)
		{
			XUserBridge.Info("未发现可装载的 mod");
			return;
		}
		LoadPreloadMods(discoveredMods.Preload);
		LoadBlMods(discoveredMods.Bl);
		SpawnHotModLoader(discoveredMods.Hot);
	}

	private static DiscoveredMods Discover(string modsDir)
	{
		DiscoveredMods discoveredMods = new DiscoveredMods();
		foreach (string item in Directory.EnumerateFileSystemEntries(modsDir))
		{
			if (Directory.Exists(item))
			{
				DiscoverPackagedMod(item, discoveredMods);
			}
			else if (File.Exists(item))
			{
				DiscoverLooseDll(modsDir, item, discoveredMods);
			}
		}
		return discoveredMods;
	}

	private static void DiscoverPackagedMod(string entryPath, DiscoveredMods discovered)
	{
		string text = Path.Combine(entryPath, "manifest.json");
		if (!File.Exists(text))
		{
			return;
		}
		string text2;
		try
		{
			text2 = File.ReadAllText(text);
		}
		catch (Exception ex)
		{
			XUserBridge.Warn("读取 mod 清单失败: " + text + " | " + ex.Message);
			return;
		}
		ModManifest modManifest = ModManifestJsonContextHolder.Parse(text2);
		if (modManifest == null)
		{
			XUserBridge.Warn("解析 mod 清单失败: " + text);
			return;
		}
		string text3 = Path.Combine(entryPath, modManifest.Entry);
		if (IsDllPath(text3) && !IsReservedSystemRuntime(text3))
		{
			string id = (string.IsNullOrEmpty(modManifest.Id) ? modManifest.Name : modManifest.Id);
			switch (modManifest.ModType)
			{
			case ModTypeBl:
				discovered.Bl.Add(new BlMod
				{
					Id = id,
					Name = modManifest.Name,
					DllPath = text3,
					ApiVersion = (modManifest.ApiVersion ?? 1),
					RequiresSymbolPack = modManifest.RequiresSymbolPack,
					RequiredSymbols = modManifest.RequiredSymbols
				});
				break;
			case ModTypeHotNative:
			case ModTypeHotInject:
				discovered.Hot.Add(new HotMod
				{
					Id = id,
					Name = modManifest.Name,
					DllPath = text3,
					InjectDelayMs = (modManifest.InjectDelayMs ?? HotInjectDefaultDelayMs),
					ReadyLevel = ResolveHotReadyLevel(modManifest),
					Required = modManifest.Required,
					VerifyExports = modManifest.VerifyExports,
					VerifyModules = modManifest.VerifyModules,
					NotifySuccess = modManifest.NotifySuccess
				});
				break;
			default:
				discovered.Preload.Add(new PreloadMod
				{
					Id = id,
					Name = modManifest.Name,
					DllPath = text3,
					Required = modManifest.Required,
					VerifyExports = modManifest.VerifyExports,
					VerifyModules = modManifest.VerifyModules,
					NotifySuccess = modManifest.NotifySuccess
				});
				break;
			}
		}
	}

	private static ReadyLevel ResolveHotReadyLevel(ModManifest manifest)
	{
		ReadyLevel readyLevel = ((manifest.ModType == ModTypeHotNative) ? ReadyLevel.Window : ReadyLevel.StableWindow);
		return manifest.InjectReady switch
		{
			"process" => ReadyLevel.Process, 
			"window" => ReadyLevel.Window, 
			"stable-window" => ReadyLevel.StableWindow, 
			_ => readyLevel, 
		};
	}

	private static void DiscoverLooseDll(string modsDir, string entryPath, DiscoveredMods discovered)
	{
		if (IsDllPath(entryPath) && !IsReservedSystemRuntime(entryPath))
		{
			PreloadMod preloadMod = PackageLooseDll(modsDir, entryPath);
			if (preloadMod != null)
			{
				discovered.Preload.Add(preloadMod);
			}
		}
	}

	private static PreloadMod? PackageLooseDll(string modsDir, string entryPath)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(entryPath);
		string fileName = Path.GetFileName(entryPath);
		string text = Path.Combine(modsDir, fileNameWithoutExtension);
		string text2 = Path.Combine(text, fileName);
		string path = Path.Combine(text, "manifest.json");
		XUserBridge.Info("自动打包散装 DLL: " + fileName);
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
			XUserBridge.Error("创建打包目录失败: " + fileNameWithoutExtension);
			return null;
		}
		if (!File.Exists(text2))
		{
			try
			{
				File.Move(entryPath, text2);
			}
			catch (Exception ex)
			{
				XUserBridge.Error("移动 DLL 失败: " + fileName + " | " + ex.Message);
				return null;
			}
		}
		if (!File.Exists(path))
		{
			ModManifest manifest = new ModManifest
			{
				Id = fileNameWithoutExtension,
				Name = fileNameWithoutExtension,
				Entry = fileName,
				ModType = ModTypePreloadNative
			};
			try
			{
				File.WriteAllText(path, ModManifestJsonContextHolder.Serialize(manifest));
			}
			catch
			{
			}
		}
		return new PreloadMod
		{
			Id = fileNameWithoutExtension,
			Name = fileNameWithoutExtension,
			DllPath = text2
		};
	}

	private static void LoadPreloadMods(List<PreloadMod> mods)
	{
		mods.Sort(delegate(PreloadMod a, PreloadMod b)
		{
			bool flag2 = a.Name == "PreLoader";
			bool flag3 = b.Name == "PreLoader";
			if (flag2 && !flag3)
			{
				return -1;
			}
			return (!flag2 & flag3) ? 1 : string.CompareOrdinal(a.Name, b.Name);
		});
		foreach (PreloadMod mod in mods)
		{
			bool num = mod.Name == "PreLoader";
			XUserBridge.Info($"Loading Mod: {mod.Name} <{Path.GetFileName(mod.DllPath)}>");
			bool flag = LoadAndVerify(mod.DllPath, mod.VerifyExports, mod.VerifyModules);
			if (num & flag)
			{
				break;
			}
		}
	}

	private unsafe static void LoadBlMods(List<BlMod> mods)
	{
		mods.Sort(delegate(BlMod a, BlMod b)
		{
			int num5 = string.CompareOrdinal(a.Name, b.Name);
			return (num5 == 0) ? string.CompareOrdinal(a.Id, b.Id) : num5;
		});
		nint num = BlHost.Initialize(Directory.GetCurrentDirectory(), Path.Combine(Directory.GetCurrentDirectory(), "mods"));
		foreach (BlMod mod in mods)
		{
			if (mod.ApiVersion != 1)
			{
				XUserBridge.Warn($"跳过 BL mod {mod.Name} ({mod.Id}): 不支持的 api_version {mod.ApiVersion}");
				continue;
			}
			if (mod.RequiresSymbolPack || mod.RequiredSymbols.Count > 0)
			{
				XUserBridge.Warn($"跳过 BL mod {mod.Name} ({mod.Id}): 符号子系统在此轻量构建中不可用");
				continue;
			}
			XUserBridge.Info($"Loading BL Mod: {mod.Name} ({mod.Id}) <{Path.GetFileName(mod.DllPath)}>");
			nint num2 = NativeMethods.LoadLibraryW(mod.DllPath);
			if (num2 == 0)
			{
				XUserBridge.Error($"BL mod 装载失败: {mod.Name} | error={Marshal.GetLastWin32Error()}");
				continue;
			}
			nint procAddress = NativeMethods.GetProcAddress(num2, "bl_mod_main_v1");
			if (procAddress == 0)
			{
				XUserBridge.Warn("BL mod 缺少入口 bl_mod_main_v1: " + mod.Name);
				continue;
			}
			nint num3 = ((delegate* unmanaged<nint, nint>)procAddress)(num);
			if (num3 == 0)
			{
				XUserBridge.Error("BL mod 返回空 ModApi: " + mod.Name);
				continue;
			}
			BlModApiV1 blModApiV = *(BlModApiV1*)num3;
			if (blModApiV.ApiVersion != 1)
			{
				XUserBridge.Warn("BL mod ModApi 版本不兼容: " + mod.Name);
			}
			else if (blModApiV.OnLoad != 0)
			{
				int num4 = ((delegate* unmanaged<nint, int>)blModApiV.OnLoad)(num);
				XUserBridge.Info((num4 == 0) ? ("BL mod 已加载: " + mod.Name) : $"BL mod on_load 返回 {num4}: {mod.Name}");
			}
		}
	}

	private static void SpawnHotModLoader(List<HotMod> mods)
	{
		if (mods.Count == 0)
		{
			return;
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			mods.Sort(delegate(HotMod a, HotMod b)
			{
				int num2 = a.InjectDelayMs.CompareTo(b.InjectDelayMs);
				if (num2 != 0)
				{
					return num2;
				}
				int num3 = a.ReadyLevel.CompareTo(b.ReadyLevel);
				return (num3 == 0) ? string.CompareOrdinal(a.Name, b.Name) : num3;
			});
			foreach (HotMod mod in mods)
			{
				bool num = WaitForReady(mod.ReadyLevel);
				Thread.Sleep((int)Math.Min(mod.InjectDelayMs, 2147483647uL));
				if (!num)
				{
					XUserBridge.Warn("hot mod 就绪等待超时，跳过: " + mod.Name);
				}
				else
				{
					XUserBridge.Info($"Loading hot mod: {mod.Name} <{Path.GetFileName(mod.DllPath)}>");
					LoadAndVerify(mod.DllPath, mod.VerifyExports, mod.VerifyModules);
				}
			}
		});
	}

	private static bool LoadAndVerify(string dllPath, List<string> verifyExports, List<string> verifyModules)
	{
		nint num = NativeMethods.LoadLibraryW(dllPath);
		if (num == 0)
		{
			XUserBridge.Error($"mod 装载失败: {Path.GetFileName(dllPath)} | error={Marshal.GetLastWin32Error()}");
			return false;
		}
		foreach (string verifyModule in verifyModules)
		{
			if (NativeMethods.GetModuleHandleW(verifyModule) == 0)
			{
				XUserBridge.Warn("mod 缺少依赖模块 " + verifyModule + ": " + Path.GetFileName(dllPath));
				return false;
			}
		}
		foreach (string verifyExport in verifyExports)
		{
			if (NativeMethods.GetProcAddress(num, verifyExport) == 0)
			{
				XUserBridge.Warn("mod 缺少导出 " + verifyExport + ": " + Path.GetFileName(dllPath));
				return false;
			}
		}
		XUserBridge.Info("mod 已装载: " + Path.GetFileName(dllPath));
		return true;
	}

	private static bool WaitForReady(ReadyLevel level)
	{
		switch (level)
		{
		case ReadyLevel.Process:
			return true;
		case ReadyLevel.Window:
			return WaitUntil(FindVisibleWindow, HotInjectWaitTimeoutMs);
		case ReadyLevel.StableWindow:
		{
			long num = Environment.TickCount64 + HotInjectWaitTimeoutMs;
			nint num2 = 0;
			while (Environment.TickCount64 < num)
			{
				nint num3 = FindVisibleWindow();
				if (num3 == 0)
				{
					num2 = 0;
					Thread.Sleep(50);
					continue;
				}
				if (num2 == 0)
				{
					num2 = num3;
					Thread.Sleep(StableWindowMs);
					continue;
				}
				if (FindVisibleWindow() == num2)
				{
					return true;
				}
				num2 = 0;
			}
			return false;
		}
		default:
			return true;
		}
	}

	private static bool WaitUntil(Func<nint> probe, int timeoutMs)
	{
		long num = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < num)
		{
			if (probe() != 0)
			{
				return true;
			}
			Thread.Sleep(100);
		}
		return false;
	}

	private static nint FindVisibleWindow()
	{
		uint pid = (uint)Environment.ProcessId;
		nint found = 0;
		NativeMethods.EnumWindows(delegate(nint hwnd, nint _)
		{
			if (NativeMethods.GetWindowThreadProcessId(hwnd, out var processId) == 0 || processId != pid)
			{
				return true;
			}
			if (!NativeMethods.IsWindowVisible(hwnd))
			{
				return true;
			}
			if (NativeMethods.HasClientAreaAtLeast(hwnd, 64, 64))
			{
				found = hwnd;
				return false;
			}
			NativeMethods.EnumWindowsProc callback = delegate(nint child, nint num)
			{
				if (NativeMethods.GetWindowThreadProcessId(child, out var processId2) != 0 && processId2 == pid && NativeMethods.IsWindowVisible(child) && NativeMethods.HasClientAreaAtLeast(child, 64, 64))
				{
					found = child;
					return false;
				}
				return true;
			};
			NativeMethods.EnumChildWindows(hwnd, callback, IntPtr.Zero);
			return found == 0;
		}, IntPtr.Zero);
		return found;
	}

	private static bool IsDllPath(string path)
	{
		return Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsReservedSystemRuntime(string path)
	{
		return Path.GetFileName(path).Equals("xgameruntime.dll", StringComparison.OrdinalIgnoreCase);
	}
}
