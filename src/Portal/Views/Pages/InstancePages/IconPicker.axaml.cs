using AsyncImageLoader;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Portal.Module.Imaging;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.InstancePages;

public partial class IconPicker : UserControl
{
    public IconPicker()
    {
        InitializeComponent();
    }

    private void BuiltInIcon_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BuiltInIcon icon } && DataContext is IconPickerViewModel viewModel)
            viewModel.SelectBuiltInIcon(icon);
    }

    private async void ChooseFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null || DataContext is not IconPickerViewModel viewModel) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图标图片",
            AllowMultiple = false,
            FileTypeFilter =
                [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }]
        });

        if (files.Count > 0)
            viewModel.SelectCustomImage(files[0]);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as IconPickerViewModel)?.Close();
    }
}

public sealed record BuiltInIcon(string ResourceName, string FileName)
{
    public string ResourceUri => $"resm:{ResourceName}?assembly=Portal.Core";


    public IAsyncImageLoader ImageLoader { get; } = new ResourceImageLoader(104);
}

public sealed record IconPickerResult(string? BuiltInResourceName, IStorageFile? CustomImageFile);

public class IconPickerViewModel : ObservableObject, IDialogContext
{
    private const string McIconResourcePrefix = "Portal.Core.Assets.McIcons.";

    public IReadOnlyList<BuiltInIcon> BuiltInIcons { get; } = typeof(MinecraftInstance).Assembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(McIconResourcePrefix, StringComparison.OrdinalIgnoreCase))
        .Where(name => Path.GetExtension(name).Equals(".png", StringComparison.OrdinalIgnoreCase))
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .Select(name => new BuiltInIcon(name, name[McIconResourcePrefix.Length..]))
        .ToList();

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public void SelectBuiltInIcon(BuiltInIcon icon)
    {
        RequestClose?.Invoke(this, new IconPickerResult(icon.ResourceName, null));
    }

    public void SelectCustomImage(IStorageFile imageFile)
    {
        RequestClose?.Invoke(this, new IconPickerResult(null, imageFile));
    }
}