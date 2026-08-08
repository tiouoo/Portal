using System.Text;
using System.Text.RegularExpressions;

namespace Portal.Core.Minecraft.Modpack;

/// <summary>
/// 与 PCL-CE 导出整合包一致的 Like 通配匹配（DotNet.Globbing 语义子集）：
/// 大小写不敏感；将 <c>#</c> 视为 <c>[0-9]</c>；支持 <c>*</c>、<c>?</c>、<c>**</c> 与字符集。
/// </summary>
internal static class ModpackGlobMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    public static bool Like(string input, string pattern)
    {
        pattern = pattern.Replace("#", "[0-9]");
        return GlobRegex(pattern).IsMatch(Normalize(input));
    }

    public static string Normalize(string path) => path.Replace('\\', '/');

    private static readonly Dictionary<string, Regex> Cached = new(StringComparer.Ordinal);

    private static Regex GlobRegex(string pattern)
    {
        lock (Cached)
        {
            if (Cached.TryGetValue(pattern, out var existing))
                return existing;

            var converted = Translate(pattern);
            Cached[pattern] = new Regex(converted,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
            return Cached[pattern];
        }
    }

    private static string Translate(string pattern)
    {
        pattern = Normalize(pattern);
        var sb = new StringBuilder("^");
        var index = 0;
        while (index < pattern.Length)
        {
            var c = pattern[index];
            if (c == '*')
            {
                var doubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                if (doubleStar)
                {
                    // ** 匹配零或多个路径段（可跨目录）
                    sb.Append(".*");
                    index += 2;
                    // **/ 后的斜杠视为可选
                    if (index < pattern.Length && pattern[index] == '/')
                    {
                        sb.Append("/?");
                        index++;
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                    index++;
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
                index++;
            }
            else if (c == '[')
            {
                index = AppendCharacterClass(pattern, index, sb);
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                index++;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }

    private static int AppendCharacterClass(string pattern, int start, StringBuilder sb)
    {
        bool negate = false;
        var index = start + 1;
        if (index < pattern.Length && pattern[index] is '!' or '^')
        {
            negate = true;
            index++;
        }

        var content = new StringBuilder();
        var closed = false;
        while (index < pattern.Length)
        {
            var c = pattern[index];
            if (c == ']')
            {
                closed = true;
                index++;
                break;
            }

            if (c == '\\') index++; // 转义，保持原义
            if (index < pattern.Length)
                content.Append(pattern[index]);
            index++;
        }

        if (!closed)
            return pattern.Length;

        sb.Append('[');
        if (negate) sb.Append('^');
        sb.Append(content);
        sb.Append(']');
        return index;
    }
}