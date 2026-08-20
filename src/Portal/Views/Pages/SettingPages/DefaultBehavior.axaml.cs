using Portal.Core.Const;
using Portal.Core.Module.AggregatedSearch;
using Portal.Module.DefaultPage;
using Portal.ViewModels;

namespace Portal.Views.Pages.SettingPages;

[AggregatedSearchPage("pages_defaultBehavior", "pages_defaultBehaviorPath", "DefaultBehavior")]
public partial class DefaultBehavior : Dsc
{
    public DefaultBehavior()
    {
        InitializeComponent();
        DataContext = this;
    }

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
}