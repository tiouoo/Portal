using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iridium.Extensions.Resources;
using Portal.Core.Minecraft.Classes;
using Portal.Core.Minecraft.Models;
using Portal.Core.Minecraft.Services;
using Portal.Localization;
using Tio.Avalonia.Standard.Modules.DiskIO;
using TioUi.Common;
using TioUi.Common.Extensions;
using TioUi.Common.Interfaces;
using TioUi.Controls;

namespace Portal.Views.Pages.InstancePages;

public sealed record ResourceVersionSwitchTarget(
    ModDetailsSource Source,
    string ProjectId,
    string CurrentVersionId,
    ResourceKind Kind,
    MinecraftInstance Instance);

public partial class ResourceVersionSwitchDialog : UserControl
{
    public ResourceVersionSwitchDialog()
    {
        InitializeComponent();
    }

    public ResourceVersionSwitchDialog(ResourceVersionSwitchTarget target)
    {
        InitializeComponent();
        ViewModel = new ResourceVersionSwitchDialogViewModel(target);
        DataContext = ViewModel;
        Loaded += async (_, _) =>
        {
            await ViewModel.LoadAsync();
            Dispatcher.UIThread.Post(() =>
            {
                if (ViewModel.SelectedItem is not null)
                    VersionListBox.ScrollIntoView(ViewModel.SelectedItem);
            }, DispatcherPriority.Background);
        };
    }

    public ResourceVersionSwitchDialogViewModel ViewModel { get; private set; } = null!;

    public static async Task<ModVersionFileItem?> ShowAsync(TopLevel topLevel, ResourceVersionSwitchTarget target)
    {
        var dialog = new ResourceVersionSwitchDialog(target);
        var result = await OverlayDialog
            .ShowCustomAsync<ModVersionFileItem?>(dialog, dialog.ViewModel, topLevel.TryGetHostId(),
                new OverlayDialogOptions
                {
                    Title = CommonLanguageManager.Instance.resourceVersionSwitch_switchVersion.CurrentValue(),
                    Buttons = DialogButton.None,
                    CanLightDismiss = false,
                    CanDragMove = true,
                    IsCloseButtonVisible = false,
                    CanResize = false,
                    VerticalAnchor = VerticalPosition.Top,
                    VerticalOffset = 110
                });
        return result;
    }
}

public sealed record ResourceVersionItem(ModVersionFileItem File, bool IsCurrent, bool IsCompatible);

public partial class ResourceVersionSwitchDialogViewModel : ObservableObject, IDialogContext
{
    private readonly ResourceVersionSwitchTarget _target;
    private List<ResourceVersionItem> _allItems = [];

    public ResourceVersionSwitchDialogViewModel(ResourceVersionSwitchTarget target)
    {
        _target = target;
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Close);
    }

    public ObservableCollection<ResourceVersionItem> VisibleItems { get; } = [];

    [ObservableProperty] public partial bool IsLoading { get; set; } = true;
    [ObservableProperty] public partial bool HasError { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool ShowAll { get; set; }
    [ObservableProperty] public partial ResourceVersionItem? SelectedItem { get; set; }
    [ObservableProperty] public partial bool CanConfirm { get; set; }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    partial void OnSearchTextChanged(string value)
    {
        RebuildVisible();
    }

    partial void OnShowAllChanged(bool value)
    {
        RebuildVisible();
    }

    partial void OnSelectedItemChanged(ResourceVersionItem? value)
    {
        CanConfirm = value is not null && !value.IsCurrent && (value.IsCompatible || ShowAll);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        try
        {
            IReadOnlyList<ModVersionFileItem> files = _target.Source switch
            {
                ModDetailsSource.Modrinth =>
                    (await IridiumResourceClients.Modrinth.GetFilesAsync(_target.ProjectId))
                    .Select(file => ModVersionFileItem.From(file.ToResourceFile())).ToArray(),
                ModDetailsSource.CurseForge => (await IridiumResourceClients.CurseForge.GetFilesAsync(
                        long.Parse(_target.ProjectId)))
                    .Select(file => ModVersionFileItem.From(file.ToResourceFile())).ToArray(),
                _ => []
            };

            _allItems = files.OrderByDescending(item => item.Published)
                .Select(file => new ResourceVersionItem(file,
                    string.Equals(file.Id, _target.CurrentVersionId, StringComparison.Ordinal),
                    ResourceCompatibility.IsCompatible(file, _target.Instance, _target.Kind)))
                .ToList();

            RebuildVisible();
            if (_allItems.Count == 0)
            {
                HasError = true;
            }
            else
            {
                SelectedItem = VisibleItems.FirstOrDefault(item => item.IsCurrent) ?? VisibleItems.FirstOrDefault();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Logger.Error($"[VersionSwitch] Failed to load versions for {_target.ProjectId}: {exception}");
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    private void Confirm()
    {
        RequestClose?.Invoke(this, SelectedItem?.File);
    }

    private void RebuildVisible()
    {
        IEnumerable<ResourceVersionItem> query = _allItems;
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(MatchesSearch);

        VisibleItems.Clear();
        foreach (var item in query.Where(item => ShowAll || item.IsCompatible || item.IsCurrent))
            VisibleItems.Add(item);

        if (SelectedItem is not null && !VisibleItems.Contains(SelectedItem))
            SelectedItem = null;
    }

    private bool MatchesSearch(ResourceVersionItem item)
    {
        var text = SearchText.Trim();
        if (text.Length == 0)
            return true;
        return item.File.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               item.File.FileName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               item.File.Details.Contains(text, StringComparison.OrdinalIgnoreCase) ||
               item.File.GroupKeys.Any(key => key.Loader.Contains(text, StringComparison.OrdinalIgnoreCase));
    }
}
