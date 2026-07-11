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
using TioUi.Common.Extensions;
using TioUi.Controls;

namespace Portal.Views.Components;

public partial class AggregatedSearchDialog : UserControl
{
    public AggregatedSearchDialog()
    {
        InitializeComponent();
        DragMove.PointerPressed += (s, e) =>
        {
            var a = (s! as Control)!.GetTopLevel() as CustomDialogWindow;
            a?.BeginMoveDrag(e);
        };

        TemplateApplied += (s, e) =>
        {
            var a = (s! as Control)!.GetTopLevel() as CustomDialogWindow;
            a.MinWidth = 680;
            a.MinHeight = 440;
            a.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    a.Close();
                }
            };
            a.Loaded += (_, e) => { SearchBox.Focus(); };
        };
    }

    private void Button_OnClick(object? s, RoutedEventArgs e)
    {
        var a = (s! as Control)!.GetTopLevel() as CustomDialogWindow;
        a?.Close();
    }
}

public partial class AggregatedSearchDialogViewModel : ObservableObject
{
    public Data Data => Data.Instance;
}

public class AggregatedSearchType
{
    public string DisplayText { get; set; }
    public AggregatedSearchEntryType EnumFlag { get; set; }
}