using System.Text;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

public static class GameOptionsService
{
    public static void SetChineseLanguage(string gameDirectory, DateTime? releaseTime)
    {
        try
        {
            SetChineseLanguageCore(gameDirectory, releaseTime);
        }
        catch (Exception exception)
        {
            Logger.Warning($"设置游戏语言失败，将不影响游戏启动。{Environment.NewLine}{exception}");
        }
    }

    private static void SetChineseLanguageCore(string gameDirectory, DateTime? releaseTime)
    {
        var optionsPath = Path.Combine(gameDirectory, "options.txt");


        if (!File.Exists(optionsPath))
        {
            var yosbrPath = Path.Combine(gameDirectory, "config", "yosbr", "options.txt");
            if (File.Exists(yosbrPath))
            {
                WriteOption(yosbrPath, "lang", "none");
                optionsPath = yosbrPath;
            }
        }


        var currentLang = ReadOption(optionsPath, "lang", "none");
        var requiredLang = ResolveChineseLanguageCode(releaseTime);
        if (string.Equals(currentLang, requiredLang, StringComparison.Ordinal))
            return;


        WriteOption(optionsPath, "lang", "-");
        WriteOption(optionsPath, "lang", requiredLang);


        var isLanguageUnconfigured = string.Equals(currentLang, "none", StringComparison.OrdinalIgnoreCase);
        var hasExistingSaves = Directory.Exists(Path.Combine(gameDirectory, "saves"));
        if (isLanguageUnconfigured || !hasExistingSaves)
            WriteOption(optionsPath, "forceUnicodeFont", "true");
    }

    private static string ResolveChineseLanguageCode(DateTime? releaseTime)
    {
        if (releaseTime.HasValue)
        {
            var time = releaseTime.Value;

            if (time > new DateTime(2000, 1, 1) && time <= new DateTime(2011, 11, 18))
                return "none";

            if (time >= new DateTime(2012, 1, 12) && time <= new DateTime(2016, 6, 8))
                return "zh_CN";
        }

        return "zh_cn";
    }

    private static string ReadOption(string path, string key, string defaultValue)
    {
        var (lines, _) = ReadLines(path);
        string? value = null;
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0) continue;
            if (!line.AsSpan(0, separatorIndex).Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            value = line[(separatorIndex + 1)..];
        }

        return value ?? defaultValue;
    }

    private static void WriteOption(string path, string key, string value)
    {
        var (lines, encoding) = ReadLines(path);
        var updated = new List<string>(lines.Length + 1);
        var replaced = false;
        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0 && line.AsSpan(0, separatorIndex).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                if (!replaced)
                {
                    updated.Add($"{key}:{value}");
                    replaced = true;
                }

                continue;
            }

            updated.Add(line);
        }

        if (!replaced)
            updated.Add($"{key}:{value}");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllLines(path, updated, encoding);
    }

    private static (string[] Lines, Encoding Encoding) ReadLines(string path)
    {
        if (!File.Exists(path))
            return ([], new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(bytes);
        var text = encoding.GetString(bytes);

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        return (lines, encoding);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(0);
        }
    }
}