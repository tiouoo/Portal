using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.International.Converters.PinYinConverter;

namespace Portal.Core.Helpers;

public static class PinyinHelper
{
    public static List<string> GetAllPinyins(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string> { string.Empty };

        var charPinyins = new List<List<string>>();
        foreach (var c in text)
        {
            var pinyins = GetCharPinyins(c);
            charPinyins.Add(pinyins);
        }

        return CartesianProduct(charPinyins);
    }

    public static List<string> GetAllFirstLetters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return new List<string> { string.Empty };

        var charPinyins = new List<List<string>>();
        foreach (var c in text)
        {
            var firstLetters = GetCharPinyins(c)
                .Select(p => p.Length > 0 ? p[0].ToString() : string.Empty)
                .Distinct()
                .ToList();
            charPinyins.Add(firstLetters);
        }

        return CartesianProduct(charPinyins);
    }

    private static List<string> GetCharPinyins(char c)
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
                    var py = pinyin.Substring(0, pinyin.Length - 1).ToLowerInvariant();
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
        return result.ToList();
    }

    private static List<string> CartesianProduct(List<List<string>> charPinyins)
    {
        IEnumerable<string> results = new[] { string.Empty };
        foreach (var pinyins in charPinyins)
        {
            results = results.SelectMany(r => pinyins.Select(p => r + p));
        }
        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
