using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Core.Classes.Config;

public partial class ShortcutConfig : ObservableObject
{
    [ObservableProperty] public partial Dictionary<string, string> Bindings { get; set; } = ShortcutActionDefaults.CreateDefaultBindings();

    public string? GetGesture(ShortcutAction action) =>
        Bindings.TryGetValue(action.ToString(), out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

        public void SetGesture(ShortcutAction action, string? gesture)
    {
        var value = string.IsNullOrWhiteSpace(gesture) ? string.Empty : gesture.Trim();
        if (value.Length > 0)
        {
            var actionKey = action.ToString();
            foreach (var key in Bindings.Where(pair => pair.Key != actionKey && pair.Value == value)
                         .Select(pair => pair.Key).ToList())
            {
                Bindings[key] = string.Empty;
            }
        }

        Bindings[action.ToString()] = value;
        OnPropertyChanged(nameof(Bindings));
    }

        public void ResetAction(ShortcutAction action)
    {
        Bindings[action.ToString()] = ShortcutActionDefaults.GetDefaultGesture(action) ?? string.Empty;
        OnPropertyChanged(nameof(Bindings));
    }
}
