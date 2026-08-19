using Portal.Localization;

namespace Portal.Core.Minecraft.Graphics;

public sealed record GraphicsLaunchConfiguration
{
    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }
    public required IReadOnlyList<string> JvmArguments { get; init; }
    public required IReadOnlyList<string> GameArguments { get; init; }
    public bool NeedsMesaAgent { get; init; }
}

public static class GraphicsLaunchArgumentsBuilder
{
    public static GraphicsLaunchConfiguration Build(
        EffectiveRenderer effective, GraphicsApi userApi, GameVersion version,
        string? nativeFolder, PlatformInfo platform)
    {
        var env = new Dictionary<string, string>();
        var jvm = new List<string>();
        var game = new List<string>();
        var mesaAgent = false;

        var renderer = effective.Renderer;

        if (platform.Os == OperatingSystemKind.Windows)
        {
            if (renderer.MesaDriverName is { } mesaDriverName)
            {
                mesaAgent = true;

                if (renderer.Api == GraphicsApi.OpenGL && renderer.Name != "LLVMPIPE")
                    env["GALLIUM_DRIVER"] = mesaDriverName;

                if (renderer.Api == GraphicsApi.Vulkan && renderer.IcdName is { } icdName)
                {
                    var icdFile = BuildMesaLoaderPath(nativeFolder, $"{icdName}_icd.json");
                    env["VK_ICD_FILENAMES"] = icdFile;
                    env["VK_DRIVER_FILES"] = icdFile;
                }

                if (nativeFolder is { Length: > 0 })
                    jvm.Add("-Dorg.glavo.mesa.loader.nativeDir=" + BuildMesaLoaderPath(nativeFolder, string.Empty));
            }
            else if (renderer.Api == GraphicsApi.Vulkan && renderer.IcdName is { } icdName)
            {
                var icdFile = FindSystemVulkanIcd(icdName, platform);
                if (icdFile is not null)
                {
                    env["VK_ICD_FILENAMES"] = icdFile;
                    env["VK_DRIVER_FILES"] = icdFile;
                }
            }
        }
        else if (platform.Os is OperatingSystemKind.Linux or OperatingSystemKind.FreeBSD)
        {
            if (renderer.Api == GraphicsApi.OpenGL)
            {
                if (renderer.Name == "LLVMPIPE")
                {
                    env["__GLX_VENDOR_LIBRARY_NAME"] = "mesa";
                    env["LIBGL_ALWAYS_SOFTWARE"] = "1";
                }
                else if (renderer.Name == "ZINK")
                {
                    env["__GLX_VENDOR_LIBRARY_NAME"] = "mesa";
                    env["MESA_LOADER_DRIVER_OVERRIDE"] = "zink";

                    env["LIBGL_KOPPER_DRI2"] = "1";
                }
            }
            else if (renderer.Api == GraphicsApi.Vulkan && renderer.IcdName is { } icdName)
            {
                var icdFile = FindSystemVulkanIcd(icdName, platform);
                if (icdFile is not null)
                {
                    env["VK_ICD_FILENAMES"] = icdFile;
                    env["VK_DRIVER_FILES"] = icdFile;
                }
            }
        }
        else if (platform.Os == OperatingSystemKind.MacOS)
        {
            if (renderer.Api == GraphicsApi.Vulkan
                && renderer.Name != "MOLTENVK"
                && renderer.IcdName is { } icdName)
            {
                var icdFile = FindSystemVulkanIcd(icdName, platform);
                if (icdFile is not null)
                {
                    env["VK_ICD_FILENAMES"] = icdFile;
                    env["VK_DRIVER_FILES"] = icdFile;
                }
            }
        }

        if (GraphicsEnvironmentResolver.ShouldPassGraphicsBackendArg(userApi, version))
        {
            game.Add("--graphicsBackend");
            game.Add(userApi.GetMinecraftArg());
        }

        return new GraphicsLaunchConfiguration
        {
            EnvironmentVariables = env,
            JvmArguments = jvm,
            GameArguments = game,
            NeedsMesaAgent = mesaAgent
        };
    }

    public static string BuildJavaAgent(string jarPath, string mesaDriverName)
    {
        if (jarPath.Contains('='))
            throw new InvalidOperationException(CommonLanguageManager.Instance.minecraft_mesaLoaderPathInvalid.CurrentValue());
        return $"{jarPath}={mesaDriverName}";
    }

    private static string BuildMesaLoaderPath(string? nativeFolder, string fileName)
    {
        return Path.Combine(Path.GetFullPath(nativeFolder ?? string.Empty), "mesa-loader", fileName);
    }

    private static string FindSystemVulkanIcd(string icdName, PlatformInfo platform)
    {
        var arch = platform.IsArm ? "aarch64" : platform.Is64Bit ? "x86_64" : "i686";
        return platform.Os switch
        {
            OperatingSystemKind.Windows => FindWindowsVulkanIcd(icdName),
            OperatingSystemKind.Linux => FindFirstExisting(
                $"/usr/share/vulkan/icd.d/{icdName}_icd.json",
                $"/etc/vulkan/icd.d/{icdName}_icd.json",
                $"/usr/share/vulkan/icd.d/{icdName}_icd.{arch}.json"),
            OperatingSystemKind.FreeBSD => FindFirstExisting(
                $"/usr/local/share/vulkan/icd.d/{icdName}_icd.json",
                $"/usr/local/share/vulkan/icd.d/{icdName}_icd.{arch}.json"),
            OperatingSystemKind.MacOS => FindFirstExisting(
                $"/usr/local/share/vulkan/icd.d/{icdName}_icd.{arch}.json",
                $"/opt/homebrew/share/vulkan/icd.d/{icdName}_icd.{arch}.json"),
            _ => null
        };
    }

    private static string FindWindowsVulkanIcd(string icdName)
    {
        var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return FindFirstExisting(
            $"{sysRoot}\\System32\\{icdName}_icd.json",
            $"{sysRoot}\\System32\\DriverStore\\FileRepository\\{icdName}_icd.json",
            $"{sysRoot}\\System32\\vulkan\\icd.d\\{icdName}_icd.json");
    }

    private static string FindFirstExisting(params string[] paths)
    {
        foreach (var path in paths)
            if (File.Exists(path))
                return Path.GetFullPath(path);
        return null;
    }
}