using Avalonia.Animation.Easings;

namespace Portal.Module.Animations;

/// <summary>
/// 幅度可调的 BackEaseOut。内置 BackEaseOut 的过冲幅度固定约 10%，
/// 用作整页滑动过渡时太晃眼；Amplitude 取 0.8 时过冲约 2%
/// </summary>
public class SoftBackEaseOut : Easing
{
    public double Amplitude { get; set; } = 0.8;

    public override double Ease(double progress)
    {
        var p = progress - 1.0;
        return 1.0 + p * p * ((Amplitude + 1.0) * p + Amplitude);
    }
}
