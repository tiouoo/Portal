using Avalonia.Controls;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class Properties : UserControl
{
    public MinecraftInstance Instance { get; }

    public Properties(MinecraftInstance instance)
    {
        Instance = instance;
        InitializeComponent();
        DataContext = this;
    }
}
