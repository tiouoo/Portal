using CommunityToolkit.Mvvm.ComponentModel;

namespace Portal.Core.Minecraft.Instance.Java;

public partial class JavaRuntimeEntry : ObservableObject, IEquatable<JavaRuntimeEntry>
{
    [ObservableProperty] public partial string JavaPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string JavaType { get; set; } = string.Empty;
    [ObservableProperty] public partial string JavaVersion { get; set; } = string.Empty;
    [ObservableProperty] public partial int MajorVersion { get; set; }
    [ObservableProperty] public partial bool Is64Bit { get; set; }

    public string DisplayName => $"Java {JavaVersion} ({JavaType})";

    public JavaRuntimeEntry()
    {
        PropertyChanged += (_, _) => Events.RaiseCoreSaveSettings();
    }

    public bool Equals(JavaRuntimeEntry? other)
    {
        return other != null && string.Equals(JavaPath, other.JavaPath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as JavaRuntimeEntry);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(JavaPath);
}
