using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations.OpenFile;

public partial class NewMinecraftFolder : UserControl
{
    public NewMinecraftFolder()
    {
        InitializeComponent();
    }
}

public partial class NewMinecraftFolderViewModel : ObservableObject, IDialogContext
{
    private readonly List<string> _paths;
    [ObservableProperty] public partial string? FolderName { get; set; }

    [ObservableProperty] public partial string? FolderPath { get; set; }
    [ObservableProperty] public partial bool Warning { get; set; }
    [ObservableProperty] public partial bool NoExist { get; set; }
    [ObservableProperty] public partial bool Contain { get; set; }

    public ICommand NextCommand { get; }
    public ICommand CancelCommand { get; }

    private readonly Dictionary<string, List<string>> _errors = new();

    public NewMinecraftFolderViewModel(List<string> paths)
    {
        _paths = paths;
        NextCommand = new RelayCommand(Next, CanNext);
        CancelCommand = new RelayCommand(Cancel);
    }

    partial void OnFolderPathChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Directory.Exists(value.Trim()))
        {
            Warning = false;
            NoExist = true;
            Contain = false;
            return;
        }

        var folderPath = value.Trim();
        FolderName = Directory.GetParent(folderPath)?.Name;
        Contain = _paths.Contains(folderPath);

        NoExist = false;

        try
        {
            var subDirPath = Path.Combine(folderPath, ".minecraft");
            Warning = Directory.Exists(subDirPath);
        }
        catch (Exception)
        {
            // ignored
        }

        ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
    }

    partial void OnFolderNameChanged(string? value)
    {
        ((RelayCommand)NextCommand).NotifyCanExecuteChanged();
    }

    private bool CanNext()
    {
        return !string.IsNullOrWhiteSpace(FolderName)
               && !string.IsNullOrWhiteSpace(FolderPath)
               && !NoExist && !Contain;
    }

    private void Next()
    {
        RequestClose?.Invoke(this, new MinecraftFolderEntry()
        {
            FolderName = FolderName.Trim(),
            FolderPath = FolderPath.Trim()
        });
    }

    private void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
}
