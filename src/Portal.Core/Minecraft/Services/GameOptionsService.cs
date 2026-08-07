using System.IO;
using System.Text;
using Tio.Avalonia.Standard.Modules.DiskIO;

namespace Portal.Core.Minecraft.Services;

/// <summary>游戏目录内 options.txt 的语言设置处理。</summary>
public static class GameOptionsService
{
    /// <summary>
    /// 在游戏启动前将 options.txt 的语言键设置为简体中文。
    /// 语言代码的大小写需要根据游戏版本区分，否则部分版本会回退为英文甚至崩溃。
    /// </summary>
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

        // Yosbr Mod 兼容：options.txt 不存在时，Yosbr 会在首次启动时写入默认语言。
        // 先将该文件内的语言键置为 none 忽略默认语言，随后再写入目标语言。
        if (!File.Exists(optionsPath))
        {
            var yosbrPath = Path.Combine(gameDirectory, "config", "yosbr", "options.txt");
            if (File.Exists(yosbrPath))
            {
                WriteOption(yosbrPath, "lang", "none");
                optionsPath = yosbrPath;
            }
        }

        // 语言代码大小写按版本区分：
        // 1.1 ~ 1.10 区域部分必须大写（zh_CN），否则会回退为英文甚至崩溃；
        // 1.11+ 区域部分必须小写（zh_cn），否则语言设置会回退为英文。
        var currentLang = ReadOption(optionsPath, "lang", "none");
        var requiredLang = ResolveChineseLanguageCode(releaseTime);
        if (string.Equals(currentLang, requiredLang, StringComparison.Ordinal))
            return;

        // 先写入 "-" 再写入目标语言，触发语言缓存变更，避免游戏沿用残留缓存
        WriteOption(optionsPath, "lang", "-");
        WriteOption(optionsPath, "lang", requiredLang);

        // 初次设置语言时一并开启 forceUnicodeFont，确保中文字形正常显示
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
            // 1.1 之前的版本没有语言选项
            if (time > new DateTime(2000, 1, 1) && time <= new DateTime(2011, 11, 18))
                return "none";
            // 1.2 ~ 1.10 区域部分必须大写
            if (time >= new DateTime(2012, 1, 12) && time <= new DateTime(2016, 6, 8))
                return "zh_CN";
        }
        // 1.11+ 区域部分必须小写
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
                // options.txt 中同一键可能出现多次，只保留一处并移除其余重复项
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
        // GetString 不会剥离 BOM，\uFEFF 会粘在首行导致键匹配失败，且改写时会叠加新的 BOM
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        return (lines, encoding);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        // 现代版本使用无 BOM 的 UTF-8；旧版本可能使用系统 ANSI 代码页（如中文系统下的 GBK）
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(true);
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(0);
        }
    }
}
