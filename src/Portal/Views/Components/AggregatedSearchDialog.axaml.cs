using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Classes.Entries;
using Portal.Const;
using Portal.Module.AggregatedSearch;
using Tio.Avalonia.Standard.Modules.Extensions;
using TioUi.Controls;

namespace Portal.Views.Components;

public partial class AggregatedSearchDialog : UserControl
{
    public AggregatedSearchDialog()
    {
        InitializeComponent();
        DragMove.PointerPressed += (s, e) =>
        {
            var a = TopLevel.GetTopLevel(s! as Control) as CustomDialogWindow;
            a?.BeginMoveDrag(e);
        };

        TemplateApplied += (s, e) =>
        {
            var a = TopLevel.GetTopLevel(s! as Control) as CustomDialogWindow;
            a.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    a.Close();
                }
            };
            a.Loaded += (_, e) =>
            {
                Data.UiProperty.AggregatedSearchResults.Clear();
                Data.UiProperty.AggregatedSearchResults.AddRange(
                    Searcher.Search(
                        Data.UiProperty.AggregatedSearchQuery, 
                        Data.UiProperty.AggregatedSelectedType.EnumFlag));
                
                SearchBox.Focus();
            };
        };
    }

    private void Button_OnClick(object? s, RoutedEventArgs e)
    {
        var a = TopLevel.GetTopLevel(s! as Control) as CustomDialogWindow;
        a?.Close();
    }

    private void SelectingItemsControl_OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (ListBox.SelectedItem is not AggregatedSearchEntry entry) return;
        var window = (DataContext as AggregatedSearchDialogViewModel).Window;

        var a = TopLevel.GetTopLevel(s! as Control) as CustomDialogWindow;
        a?.Close();

        Handler.HandleAsync(entry, window);
    }
}

public partial class AggregatedSearchDialogViewModel : ObservableObject
{
    public readonly TioWindow Window;
    public Data Data => Data.Instance;

    public AggregatedSearchDialogViewModel(TioWindow window)
    {
        Window = window;
    }
}

public class AggregatedSearchType
{
    public string DisplayText { get; set; }
    public AggregatedSearchEntryType EnumFlag { get; set; }
}