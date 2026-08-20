using System.Globalization;
using Irihi.Lingua;

namespace Portal.Localization;

public static class LocalizationService
{
    private static readonly List<ILinguaManager> Managers = [];

    public static event Action<CultureInfo>? CultureChanged;

    public static IReadOnlyList<ILinguaManager> RegisteredManagers => Managers;

    public static CultureInfo CurrentCulture =>
        Managers.Count > 0 ? Managers[0].CurrentCulture : CultureInfo.InvariantCulture;

    public static void Register(ILinguaManager manager)
    {
        if (Managers.Contains(manager))
            return;
        Managers.Add(manager);
        if (Managers.Count > 1)
            manager.UpdateCulture(Managers[0].CurrentCulture);
    }

    public static void SetCulture(CultureInfo culture)
    {
        foreach (var manager in Managers)
            manager.UpdateCulture(culture);
        CultureChanged?.Invoke(culture);
    }

    public static string ResolveKey(string key) =>
        CommonLanguageManager.Instance.GetObservable(key)?.CurrentValue() ?? key;
}
