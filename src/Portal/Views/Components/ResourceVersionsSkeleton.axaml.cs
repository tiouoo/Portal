using Avalonia.Controls;

namespace Portal.Views.Components;

public partial class ResourceVersionsSkeleton : UserControl
{
    public static IReadOnlyList<int> Items { get; } = Enumerable.Range(0, 10).ToArray();

    public ResourceVersionsSkeleton()
    {
        InitializeComponent();
    }
}
