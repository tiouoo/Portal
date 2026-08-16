namespace Portal.Core.App.Events;

using System.Diagnostics;
using Portal.Core.Minecraft.Classes;

public static class UiEvents
{
    public static event Action? BackgroundAppearanceChanged;
    public static event Action? ImageMaskChanged;
    public static event Action<double>? AppScaleChanged;
    public static Action<Process, MinecraftInstance>? ShowGameOverlay;

    public static void RaiseBackgroundAppearanceChanged() => BackgroundAppearanceChanged?.Invoke();
    public static void RaiseImageMaskChanged() => ImageMaskChanged?.Invoke();
    public static void RaiseAppScaleChanged(double scale) => AppScaleChanged?.Invoke(scale);
}
