using System.Runtime.InteropServices;
using Portal.Localization;

namespace Portal.Core.Minecraft.Graphics;

public sealed record Renderer
{
    public static readonly Renderer Default = new()
    {
        Name = "DEFAULT",
        DisplayName = CommonLanguageManager.Instance.renderer_default.CurrentValue(),
        Api = GraphicsApi.Default
    };

    public required string Name { get; init; }

    public string? DisplayName { get; init; }

    public required GraphicsApi Api { get; init; }

    public string? MesaDriverName { get; init; }

    public string? IcdName { get; init; }

    public Func<PlatformInfo, bool>? IsSupportedOverride { get; init; }

    public bool IsSupported(PlatformInfo platform)
    {
        return IsSupportedOverride?.Invoke(platform) ?? true;
    }

    public override string ToString()
    {
        return DisplayName ?? Name;
    }
}

public readonly record struct PlatformInfo
{
    public required OperatingSystemKind Os { get; init; }
    public required bool Is64Bit { get; init; }
    public bool IsArm { get; init; }
}

public enum OperatingSystemKind
{
    Unknown,
    Windows,
    Linux,
    MacOS,
    FreeBSD
}

public static class Renderers
{
    public static readonly Renderer OpenGlLlvmPipe = new()
    {
        Name = "LLVMPIPE",
        DisplayName = CommonLanguageManager.Instance.renderer_llvmSoftware.CurrentValue(),
        Api = GraphicsApi.OpenGL,
        MesaDriverName = "llvmpipe"
    };

    public static readonly Renderer OpenGlZink = new()
    {
        Name = "ZINK",
        DisplayName = "Zink (GL→Vulkan)",
        Api = GraphicsApi.OpenGL,
        MesaDriverName = "zink"
    };

    public static readonly Renderer OpenGlD3D12 = new()
    {
        Name = "D3D12",
        DisplayName = "D3D12 (GL→D3D12)",
        Api = GraphicsApi.OpenGL,
        MesaDriverName = "d3d12",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Windows
    };

    public static readonly Renderer VulkanLavaPipe = new()
    {
        Name = "LAVAPIPE",
        DisplayName = CommonLanguageManager.Instance.renderer_lavapipeSoftware.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "lvp",
        MesaDriverName = "lavapipe"
    };

    public static readonly Renderer VulkanDozen = new()
    {
        Name = "DOZEN",
        DisplayName = "Dozen (Vulkan→D3D12)",
        Api = GraphicsApi.Vulkan,
        IcdName = "dzn",
        MesaDriverName = "dzn",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Windows
    };

    public static readonly Renderer VulkanNvidia = new()
    {
        Name = "NVIDIA_VULKAN",
        DisplayName = CommonLanguageManager.Instance.renderer_nvidiaOfficial.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "nvidia"
    };

    public static readonly Renderer VulkanNvk = new()
    {
        Name = "NVIDIA_NVK",
        DisplayName = CommonLanguageManager.Instance.renderer_nvidiaNvk.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "nouveau",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Linux
    };

    public static readonly Renderer VulkanAmdvlk = new()
    {
        Name = "AMDVLK",
        DisplayName = CommonLanguageManager.Instance.renderer_amdOfficial.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "amd",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Linux
    };

    public static readonly Renderer VulkanRadv = new()
    {
        Name = "AMD_RADV",
        DisplayName = CommonLanguageManager.Instance.renderer_amdRadv.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "radeon"
    };

    public static readonly Renderer VulkanIntel = new()
    {
        Name = "INTEL_VULKAN",
        DisplayName = CommonLanguageManager.Instance.renderer_intelOfficial.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "ig",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Windows
    };

    public static readonly Renderer VulkanAnv = new()
    {
        Name = "INTEL_ANV",
        DisplayName = CommonLanguageManager.Instance.renderer_intelAnv.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "intel",
        IsSupportedOverride = platform => platform.Os != OperatingSystemKind.Windows
    };

    public static readonly Renderer VulkanHasvk = new()
    {
        Name = "INTEL_HASVK",
        DisplayName = CommonLanguageManager.Instance.renderer_intelHasvk.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "intel_hasvk",
        IsSupportedOverride = platform => platform.Os != OperatingSystemKind.Windows
    };

    public static readonly Renderer VulkanQualcomm = new()
    {
        Name = "QUALCOMM",
        DisplayName = CommonLanguageManager.Instance.renderer_qualcommOfficial.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "qc",
        IsSupportedOverride = platform => platform is { Os: OperatingSystemKind.Windows, IsArm: true }
    };

    public static readonly Renderer VulkanFreedreno = new()
    {
        Name = "TURNIP",
        DisplayName = CommonLanguageManager.Instance.renderer_turnip.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "freedreno",
        IsSupportedOverride = platform => platform.Os != OperatingSystemKind.Windows && platform.IsArm
    };

    public static readonly Renderer VulkanMoltenVk = new()
    {
        Name = "MOLTENVK",
        DisplayName = "MoltenVK (Metal)",
        Api = GraphicsApi.Vulkan,
        IcdName = "MoltenVK",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.MacOS
    };

    public static readonly Renderer VulkanKosmicKrisp = new()
    {
        Name = "KOSMICKRISP",
        DisplayName = CommonLanguageManager.Instance.renderer_kosmickrisp.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "kosmickrisp_mesa",
        IsSupportedOverride = platform => platform is { Os: OperatingSystemKind.MacOS, IsArm: true }
    };

    public static readonly Renderer VulkanPanvk = new()
    {
        Name = "PANVK",
        DisplayName = CommonLanguageManager.Instance.renderer_panvk.CurrentValue(),
        Api = GraphicsApi.Vulkan,
        IcdName = "panfrost",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Linux && platform.IsArm
    };

    private static readonly Renderer[] OpenGlAll =
    {
        OpenGlLlvmPipe, OpenGlZink, OpenGlD3D12
    };

    private static readonly Renderer[] VulkanAll =
    {
        VulkanLavaPipe, VulkanDozen, VulkanNvidia, VulkanNvk, VulkanAmdvlk, VulkanRadv,
        VulkanIntel, VulkanAnv, VulkanHasvk, VulkanQualcomm, VulkanFreedreno, VulkanMoltenVk,
        VulkanKosmicKrisp, VulkanPanvk
    };

    public static PlatformInfo CurrentPlatform => new()
    {
        Os = OperatingSystem.IsWindows() ? OperatingSystemKind.Windows
            : OperatingSystem.IsLinux() ? OperatingSystemKind.Linux
            : OperatingSystem.IsMacOS() ? OperatingSystemKind.MacOS
            : OperatingSystem.IsFreeBSD() ? OperatingSystemKind.FreeBSD
            : OperatingSystemKind.Unknown,
        Is64Bit = Environment.Is64BitOperatingSystem,
        IsArm = RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64
    };

    public static IReadOnlyList<Renderer> GetOpenGlRenderers(PlatformInfo? platform = null)
    {
        return GetSupported(platform ?? CurrentPlatform, OpenGlAll);
    }

    public static IReadOnlyList<Renderer> GetVulkanRenderers(PlatformInfo? platform = null)
    {
        return GetSupported(platform ?? CurrentPlatform, VulkanAll);
    }

    private static IReadOnlyList<Renderer> GetSupported(PlatformInfo platform, Renderer[] all)
    {
        var list = new List<Renderer>(all.Length + 1) { Renderer.Default };
        list.AddRange(all.Where(renderer => renderer.IsSupported(platform)));
        return list;
    }

    public static Renderer Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Renderer.Default;

        foreach (var renderer in OpenGlAll)
            if (renderer.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return renderer;

        foreach (var renderer in VulkanAll)
            if (renderer.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return renderer;

        return Renderer.Default;
    }
}