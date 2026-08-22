using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Portal.Module;

public static class IconResources
{
    public const string FontFamilyName = "avares://Portal/Assets/Fonts/iconfont.ttf#iconfont";

    public static FontFamily IconFont => new(FontFamilyName);

    public static Control CreateIcon(string glyph, double size)
    {
        return new TextBlock { FontFamily = IconFont, FontWeight = FontWeight.Thin, Text = glyph, FontSize = size };
    }
}
