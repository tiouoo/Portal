namespace Portal.Core.Classes.Config;

public static class ShortcutActionDefaults
{
    private static readonly Dictionary<ShortcutAction, string> DefaultGestures = new()
    {
        [ShortcutAction.NewTab] = "Ctrl+T",
        [ShortcutAction.CloseTab] = "Ctrl+W",
        [ShortcutAction.CloseAllTabs] = "Ctrl+Shift+W",
        [ShortcutAction.OpenAggregatedSearch] = "Shift+S",
        [ShortcutAction.ToggleTheme] = "Ctrl+Shift+Q",
        [ShortcutAction.OpenDebugPage] = "Shift+F12"
    };

    public static Dictionary<string, string> CreateDefaultBindings()
    {
        return DefaultGestures
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value);
    }

    public static string? GetDefaultGesture(ShortcutAction action)
    {
        return DefaultGestures.GetValueOrDefault(action);
    }
}