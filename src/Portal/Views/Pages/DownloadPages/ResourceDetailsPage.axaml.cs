using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Portal.Core.Minecraft.Models;
using Portal.Localization;
using Portal.Module;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages.DownloadPages;

public partial class ResourceDetailsPage : ResourceDetailsPageBase
{
    public ResourceDetailsPage(ResourceDetailsTarget target)
    {
        InitializeComponent();
        ViewModel = new ResourceDetailsViewModel(target);
        DataContext = ViewModel;
        ViewModel.TargetVersionGroupReady += ScrollToTargetVersionGroup;
        PageInfo = new PageInfo
        {
            Title = string.Format(CommonLanguageManager.Instance.resourceDetails_title.CurrentValue(),
                target.Definition.DisplayName),
            IconGlyph = "\ue631", IconFont = IconResources.FontFamilyName
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    /// <summary>Exposed for the XAML runtime loader; not intended for direct use.</summary>
    public ResourceDetailsPage() : this(new ResourceDetailsTarget(ResourceDefinitions.ResourcePack,
        ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public ResourceDetailsViewModel ViewModel { get; }

    private void ScrollToTargetVersionGroup(ResourceVersionGroup group)
    {
        QueueScrollTo(group);
    }

    private async void QuickInstall_OnClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            await ResourceDownload.QuickDownloadAsync(topLevel, ViewModel.Target, ViewModel.IconUrl,
                ViewModel.DisplayName);
    }

    public override void OnClose()
    {
        Logger.Info($"[Download] {ViewModel.Target.Definition.DisplayName} details page closing for {ViewModel.Name}.");
        ViewModel.TargetVersionGroupReady -= ScrollToTargetVersionGroup;
        base.OnClose();
        ViewModel.Dispose();
    }

    public static ResourceDetailsPage Create(ResourceDetailsTarget target)
    {
        return new ResourceDetailsPage(target);
    }

    public static void Open(TopLevel sender, ResourceDetailsTarget target, string? title = null)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var page = Create(target);
        OpenTab(sender, page, title ?? page.ViewModel.Name);
    }
}
