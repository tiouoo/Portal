using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using Portal.Views.Pages;
using TioUi.Common.Interfaces;

namespace Portal.Views.Components;

public partial class RandomMinecraft : UserControl
{
    public RandomMinecraft()
    {
        InitializeComponent();
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        InstanceDetailPage.Open(((sender as Control)!.Tag as MinecraftInstance)!, TopLevel.GetTopLevel(this)!);
    }
}

public class RandomMinecraftViewModle : ObservableObject, IDialogContext
{
    public RandomMinecraftViewModle(MinecraftInstance instance)
    {
        Instance = instance;
    }

    public MinecraftInstance Instance { get; set; }

    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;

    public void Complete()
    {
        RequestClose?.Invoke(this, "yes");
    }

    public void Again()
    {
        RequestClose?.Invoke(this, "again");
    }
}