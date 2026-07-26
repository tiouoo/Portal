using Avalonia.Controls;
using Portal.Core.Minecraft.Classes;

namespace Portal.Views.Pages.InstancePages;

public partial class BedrockSkins : UserControl, IDisposable
{
    public BedrockSkins()
    {
        InitializeComponent();
    }

    public BedrockSkins(MinecraftInstance instance) : this()
    {
        SkinPacksContent.Content = new SkinPacks(instance);
    }

    // 关闭标签页时把释放转发给内部列表页，否则各条目持有的图标位图只能等终结器回收
    public void Dispose() => (SkinPacksContent.Content as IDisposable)?.Dispose();
}
