using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Portal.Bedrock.Hook.Network;

internal static class NetworkHookConfig
{
	private const int PollIntervalMs = 2000;

	private static string _configPath = string.Empty;

	private static volatile bool _enableNetworkHooks;

	private static volatile bool _enableP2pRedirection;

	private static volatile string _p2pTargetIp = string.Empty;

	private static volatile ushort _networkListenPort = 19132;

	private static volatile bool _networkVerbose;

	private static HashSet<ushort> _ignorePorts = new HashSet<ushort> { 7897 };

	private static long _lastWriteUtc;

	public static bool EnableNetworkHooks => _enableNetworkHooks;

	public static bool EnableP2pRedirection => _enableP2pRedirection;

	public static string P2pTargetIp => _p2pTargetIp;

	public static ushort NetworkListenPort => _networkListenPort;

	public static bool NetworkVerbose => _networkVerbose;

	public static bool ShouldIgnorePort(ushort port)
	{
		return _ignorePorts.Contains(port);
	}

	public static void Start(string gameDir)
	{
		_configPath = Path.Combine(gameDir, "config", "Portal", "config.json");
		Reload();
		Task.Run((Func<Task?>)PollLoop);
	}

	private static async Task PollLoop()
	{
		while (true)
		{
			await Task.Delay(PollIntervalMs);
			try
			{
				if (File.Exists(_configPath) && File.GetLastWriteTimeUtc(_configPath).Ticks != Interlocked.Read(in _lastWriteUtc))
				{
					Reload();
				}
			}
			catch
			{
			}
		}
	}

	private static void Reload()
	{
		try
		{
			if (!File.Exists(_configPath))
			{
				return;
			}
			Interlocked.Exchange(ref _lastWriteUtc, File.GetLastWriteTimeUtc(_configPath).Ticks);
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(_configPath));
			JsonElement rootElement = jsonDocument.RootElement;
			_enableNetworkHooks = ReadBool(rootElement, "enable_network_hooks") || ReadBool(rootElement, "enable_p2p_redirection");
			_enableP2pRedirection = ReadBool(rootElement, "enable_p2p_redirection");
			_p2pTargetIp = ReadString(rootElement, "p2p_target_ip") ?? string.Empty;
			_networkListenPort = ReadUShort(rootElement, "network_listen_port") ?? 19132;
			_networkVerbose = ReadBool(rootElement, "network_verbose");
			_ignorePorts = ReadPorts(rootElement, "network_ignore_ports");
		}
		catch
		{
		}
	}

	private static bool ReadBool(JsonElement root, string name)
	{
		if (root.TryGetProperty(name, out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private static string? ReadString(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}
		return value.GetString();
	}

	private static ushort? ReadUShort(JsonElement root, string name)
	{
		if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt16(out var value2))
		{
			return value2;
		}
		return null;
	}

	private static HashSet<ushort> ReadPorts(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return new HashSet<ushort> { 7897 };
		}
		HashSet<ushort> hashSet = new HashSet<ushort>();
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.TryGetUInt16(out var value2))
			{
				hashSet.Add(value2);
			}
		}
		return hashSet;
	}
}
