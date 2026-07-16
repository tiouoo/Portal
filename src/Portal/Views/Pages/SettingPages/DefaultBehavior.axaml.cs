using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Portal.Const;
using Portal.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.ViewModels;
using TioUi.Common.Extensions;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("默认行为", "设置/默认行为", "DefaultBehavior")]
public partial class DefaultBehavior : DataUserControl
{
    public static IReadOnlyList<DefaultPageRegistry.DefaultPageEntry> DefaultPages => DefaultPageRegistry.Pages;

    public DefaultPageRegistry.DefaultPageEntry? SelectedDefaultPage
    {
        get => DefaultPages.FirstOrDefault(page => page.PageType.AssemblyQualifiedName == Data.ConfigEntry.DefaultPage);
        set
        {
            if (value != null)
                Data.ConfigEntry.DefaultPage = value.PageType.AssemblyQualifiedName!;
        }
    }

    public DefaultBehavior()
    {
        InitializeComponent();
        DataContext = this;
    }
}
