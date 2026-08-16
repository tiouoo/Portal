using Avalonia.Animation.Easings;

namespace Portal.Styles;

public class SoftBackEaseOut : Easing
{
    public double Amplitude { get; set; } = 0.8;

    public override double Ease(double progress)
    {
        var p = progress - 1.0;
        return 1.0 + p * p * ((Amplitude + 1.0) * p + Amplitude);
    }
}