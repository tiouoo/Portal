namespace Portal.Core;

public class Events
{
    public static event Action? CoreSaveSettings;

    public static void RaiseCoreSaveSettings()
    {
        CoreSaveSettings?.Invoke();
    }
}