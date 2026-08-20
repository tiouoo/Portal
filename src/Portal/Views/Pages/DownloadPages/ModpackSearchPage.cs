using Avalonia.Controls;

namespace Portal.Views.Pages.DownloadPages;

public sealed class ModpackSearchPage : JavaResourceSearchView
{
    public ModpackSearchPage() : base(new ModpackSearchPageViewModel())
    {
    }

    protected override Task QuickDownloadAsync(TopLevel topLevel, JavaResourceSearchResultItem item)
    {
        return ModpackInstallation.InstallFromSearchAsync(topLevel, item.Target, item.IconUrl, item.Name);
    }
}
