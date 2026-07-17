using Avalonia.Controls;
using Avalonia.Interactivity;
using Tio.Avalonia.Standard.Modules.Tasks;

namespace Portal.Views.Components;

public partial class TaskListView : UserControl
{
    public TaskListView(bool inset = false)
    {
        InitializeComponent();
        DataContext = this;
        if (inset)
            this.ScrollViewer.Width = 500;
    }

    public TaskListView()
    {
        InitializeComponent();
        DataContext = this;
    }


    public TaskManager Tasks => TaskManager.Instance;

    private void RemoveCompletedTask(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ManagedTask task }) Tasks.RemoveCompletedTask(task);
    }
}