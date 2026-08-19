using System.Globalization;
using Irihi.Lingua;

namespace Portal.Localization;

public static class LocalizationService
{
    private static readonly List<ILinguaManager> Managers = [];

    public static IReadOnlyList<ILinguaManager> RegisteredManagers => Managers;

    public static CultureInfo CurrentCulture =>
        Managers.Count > 0 ? Managers[0].CurrentCulture : CultureInfo.InvariantCulture;

    public static void Register(ILinguaManager manager)
    {
        if (!Managers.Contains(manager))
            Managers.Add(manager);
    }

    public static void SetCulture(CultureInfo culture)
    {
        foreach (var manager in Managers)
            manager.UpdateCulture(culture);
    }
}
