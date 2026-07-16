using Avalonia.Controls;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class Overview : UserControl
{
    public MinecraftInstance Instance { get; }

    public Overview(MinecraftInstance instance)
    {
        Instance = instance;
        InitializeComponent();
        DataContext = this;
    }
}
