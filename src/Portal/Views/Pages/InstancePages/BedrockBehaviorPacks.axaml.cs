using Avalonia.Controls;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockBehaviorPacks : UserControl
{
    public BedrockBehaviorPacks()
    {
        InitializeComponent();
    }

    public BedrockBehaviorPacks(MinecraftInstance instance) : this()
    {
        BehaviorPacksContent.Content = new BehaviorPacks(instance);
    }
}
