using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Views.Operations.Account;

public partial class ViewResult : UserControl
{
    public ViewResult()
    {
        InitializeComponent();
    }
}

public partial class ViewResultViewModel : ObservableObject, IDialogContext
{
    public ViewResultViewModel(ObservableCollection<MinecraftAccount> accounts)
    {
        Accounts = accounts;
        CompleteCommand = new RelayCommand(Complete);
    }

    [ObservableProperty] public partial ObservableCollection<MinecraftAccount> Accounts { get; set; } = [];

    public ICommand CompleteCommand { get; }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    private void Complete()
    {
        RequestClose?.Invoke(this, Accounts);
    }
}