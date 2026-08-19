using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Portal.Core.Minecraft.Models;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages.DownloadPages;

public partial class JavaResourceDetailsPage : ResourceDetailsPageBase
{
    protected JavaResourceDetailsPage(JavaResourceDetailsTarget target)
    {
        InitializeComponent();
        ViewModel = CreateViewModel(target);
        DataContext = ViewModel;
        ViewModel.TargetVersionGroupReady += ScrollToTargetVersionGroup;
        PageInfo = new PageInfo
        {
            Title = string.Format(CommonLanguageManager.Instance.javaResourceDetails_title.CurrentValue(),
                ViewModel.Target.Definition.DisplayName),
            Icon = StreamGeometry.Parse(JavaResourceDetailsIcon.Data)
        };
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    /// <summary>Exposed for the XAML runtime loader; not intended for direct use.</summary>
    public JavaResourceDetailsPage() : this(new JavaResourceDetailsTarget(JavaResourceDefinitions.ResourcePack,
        ModDetailsSource.Modrinth, string.Empty))
    {
    }

    public JavaResourceDetailsViewModel ViewModel { get; }

    protected virtual JavaResourceDetailsViewModel CreateViewModel(JavaResourceDetailsTarget target)
    {
        throw new NotSupportedException($"No view model factory registered for {target.Definition.Kind}.");
    }

    private void ScrollToTargetVersionGroup(JavaResourceVersionGroup group)
    {
        QueueScrollTo(group);
    }

    private async void VersionFile_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: JavaResourceFileItem file } && TopLevel.GetTopLevel(this) is { } topLevel)
            await VersionFileClickedAsync(topLevel, file);
    }

    protected virtual Task VersionFileClickedAsync(TopLevel topLevel, JavaResourceFileItem file)
    {
        return JavaResourceDownload.ShowInstallDialogAsync(topLevel, ViewModel.Target.Definition, file);
    }

    public override void OnClose()
    {
        Logger.Info($"[Download] {ViewModel.Target.Definition.DisplayName} details page closing for {ViewModel.Name}.");
        ViewModel.TargetVersionGroupReady -= ScrollToTargetVersionGroup;
        base.OnClose();
        ViewModel.Dispose();
    }

    public static JavaResourceDetailsPage Create(JavaResourceDetailsTarget target)
    {
        return target.Definition.Kind switch
        {
            JavaResourceKind.Modpack => new ModpackDetailsPage(target),
            JavaResourceKind.ShaderPack => new ShaderPackDetailsPage(target),
            JavaResourceKind.DataPack => new DataPackDetailsPage(target),
            JavaResourceKind.Save => new SaveDetailsPage(target),
            JavaResourceKind.BedrockBehaviorPack or JavaResourceKind.BedrockResourcePack
                or JavaResourceKind.BedrockWorld or JavaResourceKind.BedrockWorldTemplate =>
                new BedrockResourceDetailsPage(target),
            _ => new ResourcePackDetailsPage(target)
        };
    }

    public static void Open(TopLevel sender, JavaResourceDetailsTarget target, string title)
    {
        if (sender is not TioTabWindowBase window || string.IsNullOrWhiteSpace(target.ProjectId)) return;
        var page = Create(target);
        OpenTab(sender, page, title);
    }
}
