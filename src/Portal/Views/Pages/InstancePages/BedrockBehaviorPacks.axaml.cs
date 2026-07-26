using Avalonia.Controls;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockBehaviorPacks : UserControl, IDisposable
{
    public BedrockBehaviorPacks()
    {
        InitializeComponent();
    }

    public BedrockBehaviorPacks(MinecraftInstance instance) : this()
    {
        BehaviorPacksContent.Content = new BehaviorPacks(instance);
    }

    // 关闭标签页时把释放转发给内部列表页，否则各条目持有的图标位图只能等终结器回收
    public void Dispose() => (BehaviorPacksContent.Content as IDisposable)?.Dispose();
}
