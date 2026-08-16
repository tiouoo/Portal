using Microsoft.International.Converters.PinYinConverter;

namespace Portal.Core.App.Helpers;

public static class PinyinHelper
{
    public static List<string> GetAllPinyins(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        var charPinyins = text.Select(GetCharPinyins);
        return [.. CartesianProduct(charPinyins)];
    }

    public static List<string> GetAllFirstLetters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        var charPinyins = text.Select(c =>
            GetCharPinyins(c)
                .Select(p => p.Length > 0 ? p[0].ToString() : string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        return [.. CartesianProduct(charPinyins)];
    }

    private static IEnumerable<string> GetCharPinyins(char c)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (ChineseChar.IsValidChar(c))
            {
                var ch = new ChineseChar(c);
                foreach (var pinyin in ch.Pinyins)
                {
                    if (string.IsNullOrEmpty(pinyin)) continue;
                    var py = pinyin[..^1].ToLowerInvariant();
                    result.Add(py);
                }
            }
            else
            {
                result.Add(c.ToString());
            }
        }
        catch
        {
            result.Add(c.ToString());
        }

        return result;
    }

    private static IEnumerable<string> CartesianProduct(IEnumerable<IEnumerable<string>> charPinyins)
    {
        var results = Enumerable.Repeat(string.Empty, 1);

        results = charPinyins.Aggregate(results, (current, pinyins)
            => current.SelectMany(r => pinyins.Select(p => string.Concat(r, p))));

        return results.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}