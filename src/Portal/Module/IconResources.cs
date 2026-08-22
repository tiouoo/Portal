using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Portal.Module;

public static class IconResources
{
    public const string FontFamilyName = "avares://Portal/Assets/Fonts/iconfont.ttf#iconfont";

    private static readonly Dictionary<string, string> Glyphs = new()
    {
        { "location", "\ue613" },
        { "trash-can", "\ue640" },
        { "arrow-rotate-right", "\ue63c" },
        { "star-full", "\ue644" },
        { "star", "\ue643" },
        { "square-arrow-up-right", "\ue628" },
        { "magnifying-glass", "\ue615" },
        { "folder", "\ue611" },
        { "plus", "\ue645" },
        { "download", "\ue622" },
        { "clipboard-list", "\ue635" },
        { "circle-info", "\ue60c" },
        { "bolt", "\ue607" },
        { "close", "\ue609" },
        { "pen", "\ue62c" },
        { "link", "\ue614" },
        { "arrow-right-arrow-left", "\ue621" },
        { "ban", "\ue61f" },
        { "angle-right", "\ue612" },
        { "newspaper", "\ue62a" },
        { "file-image", "\ue632" },
        { "book", "\ue63d" },
        { "clock", "\ue637" },
        { "wrench", "\ue641" },
        { "arrow-up-from-bracket", "\ue616" },
        { "gear", "\ue627" },
        { "sun-regular", "\ue642" },
        { "moon", "\ue62e" },
        { "display", "\ue636" },
        { "user", "\ue63e" },
        { "cubes", "\ue638" },
        { "heart-solid", "\ue61c" },
        { "heart-regular", "\ue620" },
        { "minus", "\ue608" },
        { "tower-cell", "\ue602" },
        { "memory", "\ue62d" },
        { "file-waveform", "\ue649" },
        { "lightbulb", "\ue630" },
        { "microchip", "\ue62b" },
        { "image", "\ue646" },
        { "hard-drive", "\ue62f" },
        { "bug", "\ue626" },
        { "chart-simple", "\ue63b" },
        { "earth-americas", "\ue629" },
        { "database", "\ue653" },
        { "gamepad", "\ue651" },
        { "tag", "\ue619" },
        { "instalod", "\ue60f" },
        { "cloud-sun", "\ue650" },
        { "mug-hot", "\ue652" },
        { "arrow-right-from-bracket", "\ue64b" },
        { "square", "\ue64c" },
        { "resize-handle", "\ue64e" },
        { "ruler", "\ue64a" },
        { "grid", "\ue64f" },
        { "layer-group", "\ue64d" },
        { "restore", "\ue63f" },
    };

    public static FontFamily IconFont => new(FontFamilyName);

    public static string GetGlyph(string name)
    {
        return Glyphs.TryGetValue(name, out var glyph) ? glyph : string.Empty;
    }

    public static bool HasGlyph(string name)
    {
        return Glyphs.ContainsKey(name);
    }

    public static Control CreateIcon(string name, double size)
    {
        return new TextBlock { FontFamily = IconFont, Text = GetGlyph(name), FontSize = size };
    }
}

public sealed class IconGlyphExtension : MarkupExtension
{
    public IconGlyphExtension(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return IconResources.GetGlyph(Name);
    }
}

public sealed class IconGlyphConverter : Avalonia.Data.Converters.IValueConverter
{
    public static IconGlyphConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is string name ? IconResources.GetGlyph(name) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
