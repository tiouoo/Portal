namespace Portal.Core.App.Events;

public static class Events
{
    public static event Action? CoreSaveSettings;

    public static void RaiseSaveSettings()
    {
        CoreSaveSettings?.Invoke();
    }
}