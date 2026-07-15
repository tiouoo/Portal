using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using Portal.Core.Minecraft.Classes;
using TioUi.Common.Interfaces;

namespace Portal.Core.Operations;

public partial class RandomMinecraft : UserControl
{
    public RandomMinecraft()
    {
        InitializeComponent();
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