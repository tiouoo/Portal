using System.IO;
using System.Text;

namespace Portal.Core.Minecraft.Services;

/// <summary>游戏目录内 options.txt 的语言设置处理。</summary>
public static class GameOptionsService
{
    /// <summary>将游戏目录下 options.txt 的语言键设置为 zh_cn；文件不存在时创建。</summary>
    public static void SetChineseLanguage(string gameDirectory)
    {
        var optionsPath = Path.Combine(gameDirectory, "options.txt");
        if (!File.Exists(optionsPath))
        {
            Directory.CreateDirectory(gameDirectory);
            File.WriteAllText(optionsPath, "lang:zh_cn", Encoding.UTF8);
            return;
        }

        var lines = File.ReadAllLines(optionsPath, Encoding.UTF8);
        var langIndex = Array.FindIndex(lines, line => line.StartsWith("lang:", StringComparison.OrdinalIgnoreCase));
        if (langIndex >= 0)
        {
            if (lines[langIndex].Equals("lang:zh_cn", StringComparison.OrdinalIgnoreCase)) return;
            lines[langIndex] = "lang:zh_cn";
            File.WriteAllLines(optionsPath, lines, Encoding.UTF8);
            return;
        }

        var text = File.ReadAllText(optionsPath, Encoding.UTF8).TrimEnd('\r', '\n');
        File.WriteAllText(optionsPath, text + Environment.NewLine + "lang:zh_cn", Encoding.UTF8);
    }
}
