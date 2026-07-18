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

public partial class RandomMinecraftViewModle: ObservableObject, IDialogContext
{
    public MinecraftInstance Instance { get; set; }
    
    public RandomMinecraftViewModle(MinecraftInstance instance)
    {
        Instance = instance;
    }
    
    public void Complete()
    {
        RequestClose?.Invoke(this, "yes");
    }
    
    public void Again()
    {
        RequestClose?.Invoke(this, "again");
    }
    
    public void Close()
    {
        RequestClose?.Invoke(this, null);
    }

    public event EventHandler<object?>? RequestClose;
}