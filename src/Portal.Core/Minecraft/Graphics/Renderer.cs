using System.Runtime.InteropServices;

namespace Portal.Core.Minecraft.Graphics;

/// <summary>渲染器（图形驱动）抽象，基于 HMCL 的 Renderer 模型。</summary>
public sealed record Renderer
{
    public static readonly Renderer Default = new()
    {
        Name = "DEFAULT",
        DisplayName = "默认",
        Api = GraphicsApi.Default,
    };

    public required string Name { get; init; }

    public string? DisplayName { get; init; }

    public required GraphicsApi Api { get; init; }
    
    public string? MesaDriverName { get; init; }

    public string? IcdName { get; init; }

    public Func<PlatformInfo, bool>? IsSupportedOverride { get; init; }

    public bool IsSupported(PlatformInfo platform) =>
        IsSupportedOverride?.Invoke(platform) ?? true;

    public override string ToString() => DisplayName ?? Name;
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
    FreeBSD,
}

public static partial class Renderers
{
    public static PlatformInfo CurrentPlatform => new()
    {
        Os = OperatingSystem.IsWindows() ? OperatingSystemKind.Windows
            : OperatingSystem.IsLinux() ? OperatingSystemKind.Linux
            : OperatingSystem.IsMacOS() ? OperatingSystemKind.MacOS
            : OperatingSystem.IsFreeBSD() ? OperatingSystemKind.FreeBSD
            : OperatingSystemKind.Unknown,
        Is64Bit = Environment.Is64BitOperatingSystem,
        IsArm = RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64,
    };
    
    public static readonly Renderer OpenGlLlvmPipe = new()
    {
        Name = "LLVMPIPE",
        DisplayName = "LLVM (软件渲染)",
        Api = GraphicsApi.OpenGL,
        MesaDriverName = "llvmpipe",
    };

    public static readonly Renderer OpenGlZink = new()
    {
        Name = "ZINK",
        DisplayName = "Zink (GL→Vulkan)",
        Api = GraphicsApi.OpenGL,
        MesaDriverName = "zink",
    };

    public static readonly Renderer OpenGlD3D12 = new()
    {
        Name = "D3D12",
        DisplayName = "D3D12 (GL→D3D12)",
        Api = GraphicsApi.OpenGL,
        MesaDriverName = "d3d12",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Windows,
    };

    public static readonly Renderer VulkanLavaPipe = new()
    {
        Name = "LAVAPIPE",
        DisplayName = "Lavapipe (软件渲染)",
        Api = GraphicsApi.Vulkan,
        IcdName = "lvp",
        MesaDriverName = "lavapipe",
    };

    public static readonly Renderer VulkanDozen = new()
    {
        Name = "DOZEN",
        DisplayName = "Dozen (Vulkan→D3D12)",
        Api = GraphicsApi.Vulkan,
        IcdName = "dzn",
        MesaDriverName = "dzn",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Windows,
    };

    public static readonly Renderer VulkanNvidia = new()
    {
        Name = "NVIDIA_VULKAN",
        DisplayName = "NVIDIA 官方驱动",
        Api = GraphicsApi.Vulkan,
        IcdName = "nvidia",
    };

    public static readonly Renderer VulkanNvk = new()
    {
        Name = "NVIDIA_NVK",
        DisplayName = "NVIDIA NVK (开源)",
        Api = GraphicsApi.Vulkan,
        IcdName = "nouveau",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Linux,
    };

    public static readonly Renderer VulkanAmdvlk = new()
    {
        Name = "AMDVLK",
        DisplayName = "AMD 官方驱动",
        Api = GraphicsApi.Vulkan,
        IcdName = "amd",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Linux,
    };

    public static readonly Renderer VulkanRadv = new()
    {
        Name = "AMD_RADV",
        DisplayName = "AMD RADV (开源)",
        Api = GraphicsApi.Vulkan,
        IcdName = "radeon",
    };

    public static readonly Renderer VulkanIntel = new()
    {
        Name = "INTEL_VULKAN",
        DisplayName = "Intel 官方驱动",
        Api = GraphicsApi.Vulkan,
        IcdName = "ig",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Windows,
    };

    public static readonly Renderer VulkanAnv = new()
    {
        Name = "INTEL_ANV",
        DisplayName = "Intel ANV (开源)",
        Api = GraphicsApi.Vulkan,
        IcdName = "intel",
        IsSupportedOverride = platform => platform.Os != OperatingSystemKind.Windows,
    };

    public static readonly Renderer VulkanHasvk = new()
    {
        Name = "INTEL_HASVK",
        DisplayName = "Intel HASVK (开源)",
        Api = GraphicsApi.Vulkan,
        IcdName = "intel_hasvk",
        IsSupportedOverride = platform => platform.Os != OperatingSystemKind.Windows,
    };

    public static readonly Renderer VulkanQualcomm = new()
    {
        Name = "QUALCOMM",
        DisplayName = "Qualcomm 官方驱动",
        Api = GraphicsApi.Vulkan,
        IcdName = "qc",
        IsSupportedOverride = platform => platform is { Os: OperatingSystemKind.Windows, IsArm: true },
    };

    public static readonly Renderer VulkanFreedreno = new()
    {
        Name = "TURNIP",
        DisplayName = "Turnip (开源)",
        Api = GraphicsApi.Vulkan,
        IcdName = "freedreno",
        IsSupportedOverride = platform => platform.Os != OperatingSystemKind.Windows && platform.IsArm,
    };

    public static readonly Renderer VulkanMoltenVk = new()
    {
        Name = "MOLTENVK",
        DisplayName = "MoltenVK (Metal)",
        Api = GraphicsApi.Vulkan,
        IcdName = "MoltenVK",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.MacOS,
    };

    public static readonly Renderer VulkanKosmicKrisp = new()
    {
        Name = "KOSMICKRISP",
        DisplayName = "KosmicKrisp (开源)",
        Api = GraphicsApi.Vulkan,
        IcdName = "kosmickrisp_mesa",
        IsSupportedOverride = platform => platform is { Os: OperatingSystemKind.MacOS, IsArm: true },
    };

    public static readonly Renderer VulkanPanvk = new()
    {
        Name = "PANVK",
        DisplayName = "PanVK (开源)",
        Api = GraphicsApi.Vulkan,
        IcdName = "panfrost",
        IsSupportedOverride = platform => platform.Os == OperatingSystemKind.Linux && platform.IsArm,
    };

    private static readonly Renderer[] OpenGlAll =
    {
        OpenGlLlvmPipe, OpenGlZink, OpenGlD3D12,
    };

    private static readonly Renderer[] VulkanAll =
    {
        VulkanLavaPipe, VulkanDozen, VulkanNvidia, VulkanNvk, VulkanAmdvlk, VulkanRadv,
        VulkanIntel, VulkanAnv, VulkanHasvk, VulkanQualcomm, VulkanFreedreno, VulkanMoltenVk,
        VulkanKosmicKrisp, VulkanPanvk,
    };

    public static IReadOnlyList<Renderer> GetOpenGlRenderers(PlatformInfo? platform = null) =>
        GetSupported(platform ?? CurrentPlatform, OpenGlAll);

    public static IReadOnlyList<Renderer> GetVulkanRenderers(PlatformInfo? platform = null) =>
        GetSupported(platform ?? CurrentPlatform, VulkanAll);

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