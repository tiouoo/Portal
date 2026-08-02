using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Module.Widgets;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common.Interfaces;

namespace Portal.Views.Widgets;

public sealed partial class AddWidgetDialogViewModel : ObservableObject, IDialogContext
{
    private readonly WidgetWorkspace _workspace;

    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<WidgetDefinition> Items { get; } = [];

    public AddWidgetDialogViewModel(WidgetWorkspace workspace)
    {
        _workspace = workspace;
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = SearchText?.Trim();
        var list = WidgetRegistry.Definitions
            .Where(d => string.IsNullOrEmpty(keyword) ||
                        d.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        d.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Items.Clear();
        foreach (var definition in list)
            Items.Add(definition);
    }

    public void AddWidget(WidgetDefinition definition)
    {
        var host = _workspace.AddWidget(definition.Kind);
        if (host != null)
        {
            var topLevel = TopLevel.GetTopLevel(_workspace);
            topLevel?.Notice($"已添加 {definition.Name}", NotificationType.Success);
        }
    }

    public void Close() => RequestClose?.Invoke(this, null);
    public event EventHandler<object?>? RequestClose;
}

public partial class AddWidgetDialog : UserControl
{
    public AddWidgetDialog()
    {
        InitializeComponent();
    }

    private void Add_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is WidgetDefinition definition &&
            DataContext is AddWidgetDialogViewModel viewModel)
            viewModel.AddWidget(definition);
    }
}
