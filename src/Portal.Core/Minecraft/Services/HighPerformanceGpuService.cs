using System.Collections;
using Microsoft.Win32;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tmds.DBus;

namespace Portal.Core.Minecraft.Services;

public static class HighPerformanceGpuService
{
    private const string SwitcherooService = "net.hadess.SwitcherooControl";
    private const string SwitcherooInterface = "net.hadess.SwitcherooControl";
    private const string SwitcherooObjectPath = "/net/hadess/SwitcherooControl";

    public static void TrySetWindowsHighPerformanceGpuPreference(string executablePath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(executablePath))
            return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
            const string preference = "GpuPreference=2;";
            if (!string.Equals(key.GetValue(executablePath) as string, preference, StringComparison.Ordinal))
                key.SetValue(executablePath, preference, RegistryValueKind.String);
        }
        catch (Exception exception)
        {
            Logger.Warning($"设置 Java 高性能显卡首选项失败。{Environment.NewLine}{exception}");
        }
    }

    public static async Task<IReadOnlyDictionary<string, string>>
        ResolveLinuxHighPerformanceGpuEnvironmentAsync()
    {
        try
        {
            var environment = await QuerySwitcherooEnvironmentAsync();
            if (environment is { Count: > 0 })
            {
                Logger.Info("正在使用 switcheroo-control 提供的高性能显卡环境变量");
                return environment;
            }
        }
        catch (Exception exception)
        {
            Logger.Debug($"查询 switcheroo-control 失败，将回退到 PRIME 渲染卸载变量。{Environment.NewLine}{exception}");
        }

        return CreatePrimeFallbackEnvironment();
    }

    private static async Task<IReadOnlyDictionary<string, string>?> QuerySwitcherooEnvironmentAsync()
    {
        using var connection = new Connection(Address.System);
        await connection.ConnectAsync();
        var proxy = connection.CreateProxy<ISwitcherooControl>(SwitcherooService,
            new ObjectPath(SwitcherooObjectPath));

        var value = await proxy.GetAsync("GPUs");
        if (value is not IEnumerable gpus)
            return null;

        foreach (var gpuObject in gpus)
        {
            if (gpuObject is not IDictionary<string, object> gpu)
                continue;

            if (gpu.TryGetValue("Default", out var defaultValue) && defaultValue is true)
                continue;

            if (!gpu.TryGetValue("Environment", out var environmentValue) ||
                environmentValue is not IEnumerable<string> entries)
                continue;

            var list = entries.ToList();
            if (list.Count == 0 || list.Count % 2 != 0)
                continue;

            var result = new Dictionary<string, string>();
            for (var index = 0; index < list.Count; index += 2)
                if (!string.IsNullOrEmpty(list[index]))
                    result[list[index]] = list[index + 1] ?? string.Empty;

            if (result.Count > 0)
                return result;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> CreatePrimeFallbackEnvironment()
    {
        return new Dictionary<string, string>
        {
            ["DRI_PRIME"] = "1",
            ["__NV_PRIME_RENDER_OFFLOAD"] = "1",
            ["__VK_LAYER_NV_optimus"] = "NVIDIA_only",
            ["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia"
        };
    }

    [DBusInterface(SwitcherooInterface)]
    public interface ISwitcherooControl : IDBusObject
    {
        Task<object> GetAsync(string propertyName);
    }
}