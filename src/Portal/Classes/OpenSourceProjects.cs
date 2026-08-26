using Portal.Localization;

namespace Portal.Classes;

public static class OpenSourceProjects
{
    public static IReadOnlyList<OpenSourceProject> All { get; } =
    [
        new("Avalonia", "MIT License", "https://github.com/AvaloniaUI/Avalonia"),
        new("AsyncImageLoader.Avalonia", "MIT License", "https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia"),
        new("CommunityToolkit.Mvvm", "MIT License", "https://github.com/CommunityToolkit/dotnet"),
        new("CompositionMaterial.Avalonia", "MIT License",
            "https://github.com/HelloWRC/CompositionMaterial.Avalonia"),
        new("DotNet.Bundle", "MIT License", "https://github.com/Tyrrrz/DotnetBundle"),
        new("fNbt", "BSD 3-Clause License", "https://github.com/mstefarov/fNbt"),
        new("Flurl.Http", "MIT License", "https://flurl.dev/"),
        new("Hardware.Info", "MIT License", "https://github.com/Jinjinov/Hardware.Info"),
        new("HotAvalonia", "MIT License", "https://github.com/Kira-NT/HotAvalonia"),
        new("Html Agility Pack", "MIT License", "https://github.com/zzzprojects/html-agility-pack"),
        new("Microsoft.Data.Sqlite", "MIT License", "https://github.com/dotnet/efcore"),
        new("SQLitePCLRaw", "Apache License 2.0", "https://github.com/ericsink/SQLitePCL.raw"),
        new("NbtToolkit", "MIT License", "https://github.com/gaviny82/NbtToolkit"),
        new("PeNet", "Apache License 2.0", "https://github.com/secana/PeNet"),
        new("PinYinConverterCore", "MIT License", "https://github.com/netcorepal/PinYinConverterCore"),
        new("PolySharp", "MIT License", "https://github.com/Sergio0694/PolySharp"),
        new("SharpCompress", "MIT License", "https://github.com/adamhathcock/sharpcompress"),
        new("SkiaSharp", "MIT License", "https://github.com/mono/SkiaSharp"),
        new("SmoothScroll.Avalonia", "MIT License", "https://github.com/alienator88/SmoothScroll.Avalonia"),
        new("Tomlyn", "BSD 2-Clause License", "https://github.com/xoofx/Tomlyn"),
        new("BLoader", "GNU GPL v3.0", "https://github.com/Chlna6666/BLoader"),
        new("WineGDK", "GNU LGPL v2.1+", "https://github.com/winegdk/winegdk"),
        new("MinecraftLaunch", "MIT License", "https://github.com/tiouoo/MinecraftLaunch"),
        new("Iridium", "MIT License", "https://github.com/Lunova-Studio/Iridium"),
        new("LiteSkinViewer", "MIT License", "https://github.com/tiouoo/LiteSkinViewer"),
        new("Tio.Avalonia.Standard", "MIT License", "https://github.com/tiouoo/Tio.Avalonia.Standard"),
        new("TioUi.Avalonia", "MIT License", "https://github.com/tiouoo/TioUi.Avalonia"),
        new("PreLoadCpp", "GNU GPL v3.0", "https://github.com/Round-Studio/PreLoadCpp"),
        new("Uwp.Injector", "Apache License 2.0", "https://github.com/Round-Studio/Uwp.Injector"),
        new("GravityCone", "MIT License", "https://github.com/Tianpao/GravityCone"),
        new("EasyTier", "GNU LGPL v3.0", "https://github.com/EasyTier/EasyTier"),
        new("GDK-Proton", CommonLanguageManager.Instance.about_noLicense.CurrentValue(),
            "https://github.com/Weather-OS/GDK-Proton")
    ];
}

public sealed record OpenSourceProject(string Name, string License, string Url);
