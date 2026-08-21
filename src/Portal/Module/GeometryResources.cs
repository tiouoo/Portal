using Avalonia;
using Avalonia.Media;

namespace Portal.Module;

public static class GeometryResources
{
    public static StreamGeometry Get(string key)
    {
        return Application.Current!.Resources.TryGetResource(key, null, out var resource) &&
               resource is StreamGeometry geometry
            ? geometry
            : StreamGeometry.Parse("M0 0z");
    }
}
