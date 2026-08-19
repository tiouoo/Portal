using Portal.Localization;

namespace Portal.LitematicaViewer.Helpers;

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
        if (number == 0) return string.Format(CommonLanguageManager.Instance.litematica_unitCount.CurrentValue(), 0);

        var largeChest = 54L * 27 * 64;
        var shulkerBox = 27L * 64;
        var stack = 64L;

        var result = "";
        if (number >= largeChest)
        {
            result += string.Format(CommonLanguageManager.Instance.litematica_unitChest.CurrentValue(), number / largeChest);
            number %= largeChest;
        }
        if (number >= shulkerBox)
        {
            result += string.Format(CommonLanguageManager.Instance.litematica_unitShulkerBox.CurrentValue(), number / shulkerBox);
            number %= shulkerBox;
        }
        if (number >= stack)
        {
            result += string.Format(CommonLanguageManager.Instance.litematica_unitStack.CurrentValue(), number / stack);
            number %= stack;
        }
        if (number > 0)
            result += string.Format(CommonLanguageManager.Instance.litematica_unitCount.CurrentValue(), number);

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
