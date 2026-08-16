using System.Runtime.InteropServices;

namespace Portal.Core.Minecraft.Graphics;

public sealed record MesaLoaderArtifact
{
    public static readonly MesaLoaderArtifact X64 = new()
    {
        Name = "org.glavo:mesa-loader-windows:26.0.4:x64",
        Url = "https://repo1.maven.org/maven2/org/glavo/mesa-loader-windows/26.0.4/mesa-loader-windows-26.0.4-x64.jar",
        Size = 49873846,
        Sha1 = "f8da709c59ef61f531c91434ca0e3b4f39202981"
    };

    public static readonly MesaLoaderArtifact X86 = new()
    {
        Name = "org.glavo:mesa-loader-windows:26.0.4:x86",
        Url = "https://repo1.maven.org/maven2/org/glavo/mesa-loader-windows/26.0.4/mesa-loader-windows-26.0.4-x86.jar",
        Size = 41742113,
        Sha1 = "ac6afaa8baa7c17468267c09e77e1296ee92d5ed"
    };

    public static readonly MesaLoaderArtifact Arm64 = new()
    {
        Name = "org.glavo:mesa-loader-windows:26.0.4:arm64",
        Url =
            "https://repo1.maven.org/maven2/org/glavo/mesa-loader-windows/26.0.4/mesa-loader-windows-26.0.4-arm64.jar",
        Size = 212284737,
        Sha1 = "6b1c10cfe9e20d3f50e4ae8c8b2313a5b3a94cde"
    };

    public required string Name { get; init; }
    public required string Url { get; init; }
    public required long Size { get; init; }
    public required string Sha1 { get; init; }

    public static MesaLoaderArtifact? ForCurrentPlatform(OperatingSystemKind os)
    {
        return os switch
        {
            OperatingSystemKind.Windows => (RuntimeInformation.ProcessArchitecture,
                    Environment.Is64BitOperatingSystem) switch
                {
                    (Architecture.X64, _) => X64,
                    (Architecture.X86, _) => X86,
                    (Architecture.Arm64, _) => Arm64,
                    _ => null
                },
            _ => null
        };
    }
}

public sealed record EffectiveRenderer
{
    public required GraphicsApi Api { get; init; }
    public required Renderer Renderer { get; init; }
}

public static class GraphicsEnvironmentResolver
{
    private static readonly GameVersion Version26_2Snap2 = GameVersion.Parse("26.2-snapshot-2");

    public static GraphicsApi ResolveApi(GraphicsApi configured, GameVersion version)
    {
        return configured == GraphicsApi.Default
            ? GraphicsApiExtensions.GetDefault(version)
            : configured;
    }

    public static EffectiveRenderer Resolve(GraphicsApi configuredApi, string? openGlRenderer, string? vulkanRenderer,
        GameVersion version)
    {
        var api = ResolveApi(configuredApi, version);

        var renderer = api switch
        {
            GraphicsApi.OpenGL => Select(api, Renderers.Resolve(openGlRenderer)),
            GraphicsApi.Vulkan => Select(api, Renderers.Resolve(vulkanRenderer)),
            _ => Renderer.Default
        };

        return new EffectiveRenderer { Api = api, Renderer = renderer };
    }

    private static Renderer Select(GraphicsApi api, Renderer renderer)
    {
        return renderer.Api == api ? renderer : Renderer.Default;
    }

    public static bool ShouldPassGraphicsBackendArg(GraphicsApi configuredApi, GameVersion version)
    {
        return configuredApi is not GraphicsApi.Default &&
               version >= Version26_2Snap2;
    }
}