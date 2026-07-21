using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Instance;
using Portal.Core.Minecraft.Instance.Bedrock;
using Portal.Core.Minecraft.Services;
using Tio.Avalonia.Standard.Tab.Gateway;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockPackageImportDialog : UserControl
{
    public BedrockPackageImportDialog() => InitializeComponent();
    private void Import_Click(object? sender, RoutedEventArgs e) => (DataContext as BedrockPackageImportDialogViewModel)?.Import();
    private void Cancel_Click(object? sender, RoutedEventArgs e) => (DataContext as BedrockPackageImportDialogViewModel)?.Cancel();

    public static async Task ImportAsync(TopLevel topLevel, string archivePath, BedrockPackageInspection inspection)
    {
        var result = await OverlayDialog.ShowCustomAsync<BedrockPackageImportDialog, BedrockPackageImportDialogViewModel,
            BedrockPackageImportDialogResult>(new BedrockPackageImportDialogViewModel(inspection), topLevel.TryGetHostId(),
            new OverlayDialogOptions
            {
                Title = "导入基岩版包", Buttons = DialogButton.None, CanLightDismiss = false, CanResize = false
            });
        if (result == null) return;

        try
        {
            await Task.Run(() => new BedrockPackageImportService().Import(archivePath, inspection, result.Instance,
                result.WorldUserId));
            NotificationGateway.Notice(topLevel, $"{Path.GetFileName(archivePath)} 已导入", NotificationType.Success);
        }
        catch (Exception ex)
        {
            NotificationGateway.Notice(topLevel, $"导入失败：{ex.Message}", NotificationType.Error);
        }
    }
}

public sealed record BedrockPackageImportDialogResult(MinecraftInstance Instance, string? WorldUserId);
public sealed record BedrockPackageInstanceItem(MinecraftInstance Instance, string Name, string Version);

public partial class BedrockPackageImportDialogViewModel : ObservableObject, IDialogContext
{
    private readonly BedrockPackageInspection _inspection;
    public ObservableCollection<BedrockPackageInstanceItem> Instances { get; } = [];
    public ObservableCollection<string> WorldUserIds { get; } = [];
    public bool HasNoInstances => Instances.Count == 0;
    public bool RequiresUserId => _inspection.ArchiveType == BedrockPackageArchiveType.Mcworld;
    public bool CanImport => SelectedInstance != null && (!RequiresUserId || !string.IsNullOrWhiteSpace(SelectedWorldUserId));
    public string PackageDescription => RequiresUserId ? $"{_inspection.DisplayName}（存档）" :
        $"{_inspection.DisplayName}（{string.Join("、", _inspection.Contents.Select(content => content.Type switch
        {
            BedrockPackageContentType.ResourcePack => "资源包",
            BedrockPackageContentType.BehaviorPack => "行为包",
            BedrockPackageContentType.SkinPack => "皮肤包",
            BedrockPackageContentType.WorldTemplate => "世界模板",
            _ => "基岩版包"
        }).Distinct())}）";

    [ObservableProperty] public partial BedrockPackageInstanceItem? SelectedInstance { get; set; }
    [ObservableProperty] public partial string? SelectedWorldUserId { get; set; }

    public BedrockPackageImportDialogViewModel(BedrockPackageInspection inspection)
    {
        _inspection = inspection;
        foreach (var instance in InstanceManager.Instance.Instances.Where(instance => instance.IsBedrock))
            Instances.Add(new BedrockPackageInstanceItem(instance, instance.InstanceName, instance.VersionId));
        SelectedInstance = Instances.FirstOrDefault();
    }

    partial void OnSelectedInstanceChanged(BedrockPackageInstanceItem? value)
    {
        WorldUserIds.Clear();
        if (RequiresUserId && value?.Instance.BedrockConfig is { } config)
            foreach (var userId in BedrockDataPathResolver.GetWorldUserIds(config)) WorldUserIds.Add(userId);
        SelectedWorldUserId = WorldUserIds.FirstOrDefault(userId => !string.Equals(userId, "Shared", StringComparison.OrdinalIgnoreCase))
                              ?? WorldUserIds.FirstOrDefault();
        OnPropertyChanged(nameof(CanImport));
    }

    partial void OnSelectedWorldUserIdChanged(string? value) => OnPropertyChanged(nameof(CanImport));
    public void Import()
    {
        if (SelectedInstance != null && CanImport)
            RequestClose?.Invoke(this, new BedrockPackageImportDialogResult(SelectedInstance.Instance, SelectedWorldUserId));
    }
    public void Cancel() => RequestClose?.Invoke(this, null);
    public void Close() => Cancel();
    public event EventHandler<object?>? RequestClose;
}
