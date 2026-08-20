using System.Globalization;
using Irihi.Lingua;

namespace Portal.Localization;

public static class LocalizationService
{
    private static readonly List<ILinguaManager> Managers = [];
    private static CultureInfo? _pendingCulture;

    public static event Action<CultureInfo>? CultureChanged;

    public static IReadOnlyList<ILinguaManager> RegisteredManagers => Managers;

    public static CultureInfo CurrentCulture =>
        _pendingCulture ?? (Managers.Count > 0 ? Managers[0].CurrentCulture : CultureInfo.InvariantCulture);

    public static void Register(ILinguaManager manager)
    {
        if (Managers.Contains(manager))
            return;
        Managers.Add(manager);
        if (_pendingCulture is { } culture)
            manager.UpdateCulture(culture);
        else if (Managers.Count > 1)
            manager.UpdateCulture(Managers[0].CurrentCulture);
    }

    public static void SetCulture(CultureInfo culture)
    {
        _pendingCulture = culture;
        foreach (var manager in Managers)
            manager.UpdateCulture(culture);
        CultureChanged?.Invoke(culture);
    }

    public static string ResolveKey(string key) =>
        CommonLanguageManager.Instance.GetObservable(key)?.CurrentValue() ?? key;
}
