namespace LitematicaViewer.Core.Helpers;

public static class UnitConverter
{
    public static string Convert(long number, string lang = "zh")
    {
        if (lang == "zh")
            return ConvertZh(number);
        return ConvertEn(number);
    }

    private static string ConvertZh(long number)
    {
        if (number == 0) return "0个";

        var largeChest = 54L * 27 * 64;
        var shulkerBox = 27L * 64;
        var stack = 64L;

        var result = "";
        if (number >= largeChest)
        {
            result += $"{number / largeChest}箱";
            number %= largeChest;
        }
        if (number >= shulkerBox)
        {
            result += $"{number / shulkerBox}盒";
            number %= shulkerBox;
        }
        if (number >= stack)
        {
            result += $"{number / stack}组";
            number %= stack;
        }
        if (number > 0)
            result += $"{number}个";

        return result;
    }

    private static string ConvertEn(long number)
    {
        if (number == 0) return "0U";

        var largeChest = 54L * 27 * 64;
        var shulkerBox = 27L * 64;
        var stack = 64L;

        var result = "";
        if (number >= largeChest)
        {
            result += $"{number / largeChest}LC";
            number %= largeChest;
        }
        if (number >= shulkerBox)
        {
            result += $"{number / shulkerBox}SB";
            number %= shulkerBox;
        }
        if (number >= stack)
        {
            result += $"{number / stack}S";
            number %= stack;
        }
        if (number > 0)
            result += $"{number}U";

        return result;
    }
}
