using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Tio.Avalonia.Standard.Modules.Extensions;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

public partial class TaskPage : DataUserControl, ITioTabPage
{
    public TaskPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    public PageInfo PageInfo { get; init; } = new()
    {
        Title = "任务",
        Icon = StreamGeometry.Parse("M128,64H512A64,64 0 0 1 576,128V512A64,64 0 0 1 512,576H128A64,64 0 0 1 64,512V128A64,64 0 0 1 128,64M160,160V224H480V160H160M160,288V352H480V288H160M160,416V480H352V416H160Z")
    };

    public TabEntry HostTab { get; set; }
}
