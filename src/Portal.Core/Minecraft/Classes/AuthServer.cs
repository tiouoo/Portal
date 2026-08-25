using System.ComponentModel;

namespace Portal.Core.Minecraft.Classes;

public record AuthServer(AccountType AuthType, string DisplayText) : INotifyPropertyChanged
{
    private bool _isSelected;

    public AccountType AuthType { get; set; } = AuthType;
    public string DisplayText { get; set; } = DisplayText;
    public string IconGlyph { get; set; } = "\ue614";
    public string ServerUrl { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
