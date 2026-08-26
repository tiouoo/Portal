using Avalonia.Controls;

namespace Portal.Views.Components;

public partial class ResourceSearchSkeleton : UserControl
{
    public static IReadOnlyList<int> Items { get; } = Enumerable.Range(0, 80).ToArray();

    public ResourceSearchSkeleton()
    {
        InitializeComponent();
    }
}
