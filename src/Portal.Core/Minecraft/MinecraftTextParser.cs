using System.Text;

namespace Portal.Core.Minecraft;

public sealed class MinecraftTextSegment
{
    public required string Text { get; init; }
    public string? ColorHex { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
    public bool Obfuscated { get; init; }
}

public static class MinecraftTextParser
{
    private static readonly string[] LegacyColors =
    {
        "000000", "0000AA", "00AA00", "00AAAA", "AA0000", "AA00AA", "FFAA00", "AAAAAA",
        "555555", "5555FF", "55FF55", "55FFFF", "FF5555", "FF55FF", "FFFF55", "FFFFFF"
    };

    public static IReadOnlyList<MinecraftTextSegment> Parse(string? text)
    {
        var segments = new List<MinecraftTextSegment>();
        if (string.IsNullOrEmpty(text))
            return segments;

        var builder = new StringBuilder();
        string? color = null;
        var bold = false;
        var italic = false;
        var underline = false;
        var strikethrough = false;
        var obfuscated = false;

        void Flush()
        {
            if (builder.Length == 0)
                return;

            segments.Add(new MinecraftTextSegment
            {
                Text = builder.ToString(),
                ColorHex = color,
                Bold = bold,
                Italic = italic,
                Underline = underline,
                Strikethrough = strikethrough,
                Obfuscated = obfuscated
            });
            builder.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '§')
            {
                if (++i >= text.Length)
                    break;

                var code = char.ToLowerInvariant(text[i]);
                Flush();
                switch (code)
                {
                    case >= '0' and <= '9':
                    case >= 'a' and <= 'f':
                    {
                        var index = code <= '9' ? code - '0' : code - 'a' + 10;
                        color = $"#{LegacyColors[index]}";
                        bold = italic = underline = strikethrough = obfuscated = false;
                        break;
                    }
                    case 'k':
                        obfuscated = true;
                        break;
                    case 'l':
                        bold = true;
                        break;
                    case 'm':
                        strikethrough = true;
                        break;
                    case 'n':
                        underline = true;
                        break;
                    case 'o':
                        italic = true;
                        break;
                    case 'r':
                        color = null;
                        bold = italic = underline = strikethrough = obfuscated = false;
                        break;
                    case 'x':

                        if (i + 12 < text.Length && TryReadRgbCode(text, i + 1, out var hex))
                        {
                            color = $"#{hex}";
                            bold = italic = underline = strikethrough = obfuscated = false;
                            i += 12;
                        }

                        break;
                }

                continue;
            }

            if (c == '#' && i + 6 < text.Length && TryReadHex(text, i + 1, out var hexColor))
            {
                Flush();
                color = $"#{hexColor}";
                bold = italic = underline = strikethrough = obfuscated = false;
                i += 6;
                continue;
            }

            builder.Append(c);
        }

        Flush();
        return segments;
    }

    private static bool TryReadHex(string text, int start, out string hex)
    {
        hex = string.Empty;
        if (start + 6 > text.Length)
            return false;

        for (var i = 0; i < 6; i++)
            if (!IsHexDigit(text[start + i]))
                return false;

        hex = text.Substring(start, 6);
        return true;
    }

    private static bool TryReadRgbCode(string text, int start, out string hex)
    {
        hex = string.Empty;
        var chars = new char[6];
        for (var i = 0; i < 6; i++)
        {
            if (text[start + i * 2] != '§' || !IsHexDigit(text[start + i * 2 + 1]))
                return false;

            chars[i] = char.ToUpperInvariant(text[start + i * 2 + 1]);
        }

        hex = new string(chars);
        return true;
    }

    private static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}