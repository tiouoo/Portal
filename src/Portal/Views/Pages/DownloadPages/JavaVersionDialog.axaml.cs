using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Const;
using Portal.Core.Services;
using TioUi.Common.Interfaces;

namespace Portal.Views.Pages.DownloadPages;

public partial class JavaVersionDialog : UserControl
{
    public JavaVersionDialog()
    {
        InitializeComponent();
    }

    private void Confirm_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as JavaVersionDialogViewModel)?.Confirm();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as JavaVersionDialogViewModel)?.Cancel();
    }
}

public partial class JavaVersionOption : ObservableObject
{
    public JavaVersionOption(JavaDistributionVersion version, bool isInstalled)
    {
        Version = version;
        IsInstalled = isInstalled;
    }

    public JavaDistributionVersion Version { get; }
    public string DisplayName => $"Java {Version.MajorVersion}";
    public string DetailText => $"{Version.FullVersion} · {Version.Vendor}";
    public bool IsInstalled { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}

public partial class JavaVersionDialogViewModel : ObservableObject, IDialogContext
{
    public JavaVersionDialogViewModel(JavaDistributionItem distribution)
    {
        HeaderText = $"选择要安装的 {distribution.DisplayName} 版本";
        var installedMajors = Data.ConfigEntry.JavaRuntimes
            .Where(x => x.JavaPath.StartsWith(ConfigPath.JavaRuntimesPath, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.MajorVersion)
            .ToHashSet();
        foreach (var version in distribution.Versions.OrderByDescending(x => x.MajorVersion))
            Versions.Add(new JavaVersionOption(version, installedMajors.Contains(version.MajorVersion)));
        SelectedOption = Versions.FirstOrDefault();
    }

    public ObservableCollection<JavaVersionOption> Versions { get; } = [];
    public string HeaderText { get; }

    [ObservableProperty] public partial JavaVersionOption? SelectedOption { get; set; }

    public bool CanConfirm => SelectedOption is not null;

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    partial void OnSelectedOptionChanged(JavaVersionOption? value)
    {
        OnPropertyChanged(nameof(CanConfirm));
    }

    public void Confirm()
    {
        RequestClose?.Invoke(this, SelectedOption?.Version);
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}