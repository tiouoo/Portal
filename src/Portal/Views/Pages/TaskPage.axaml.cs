using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Tio.Avalonia.Standard.Modules.Extensions;
using Portal.Module.AggregatedSearch;
using Portal.ViewModels;
using Tio.Avalonia.Standard.Tab.Entries;
using Tio.Avalonia.Standard.Tab.Interface;

namespace Portal.Views.Pages;

[AggregatedSearchPage("任务", "任务", "Task")]
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
        Icon = StreamGeometry.Parse("F1 M640,640z M0,0z M439.4,96L448,96C483.3,96,512,124.7,512,160L512,512C512,547.3,483.3,576,448,576L192,576C156.7,576,128,547.3,128,512L128,160C128,124.7,156.7,96,192,96L200.6,96C211.6,76.9,232.3,64,256,64L384,64C407.7,64,428.4,76.9,439.4,96z M376,176C389.3,176 400,165.3 400,152 400,138.7 389.3,128 376,128L264,128C250.7,128 240,138.7 240,152 240,165.3 250.7,176 264,176L376,176z M256,320C256,302.3 241.7,288 224,288 206.3,288 192,302.3 192,320 192,337.7 206.3,352 224,352 241.7,352 256,337.7 256,320z M288,320C288,333.3,298.7,344,312,344L424,344C437.3,344 448,333.3 448,320 448,306.7 437.3,296 424,296L312,296C298.7,296,288,306.7,288,320z M288,448C288,461.3,298.7,472,312,472L424,472C437.3,472 448,461.3 448,448 448,434.7 437.3,424 424,424L312,424C298.7,424,288,434.7,288,448z M224,480C241.7,480 256,465.7 256,448 256,430.3 241.7,416 224,416 206.3,416 192,430.3 192,448 192,465.7 206.3,480 224,480z")
    };

    public TabEntry HostTab { get; set; }

    public void OnClose()
    {
        DataContext = null;
    }
}
