using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Portal.Core.Minecraft;

namespace Portal.Views.Controls;

public class MinecraftTextBlock : TextBlock
{
    private const double ObfuscatedTickIntervalMs = 20;

    private static readonly char[] ObfuscatedChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()".ToCharArray();

    private static readonly Random Random = new();
    private readonly DispatcherTimer _obfuscatedTimer;
    private readonly List<Run> _obfuscatedRuns = [];

    static MinecraftTextBlock()
    {
        TextProperty.Changed.AddClassHandler<MinecraftTextBlock>((block, e) => block.RenderSegments());
    }

    public MinecraftTextBlock()
    {
        _obfuscatedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ObfuscatedTickIntervalMs) };
        _obfuscatedTimer.Tick += (_, _) => TickObfuscated();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _obfuscatedTimer.Stop();
        _obfuscatedRuns.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void RenderSegments()
    {
        _obfuscatedTimer.Stop();
        _obfuscatedRuns.Clear();

        var inlines = new InlineCollection();
        var segments = MinecraftTextParser.Parse(Text);
        foreach (var segment in segments)
        {
            var lines = segment.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    inlines.Add(new LineBreak());

                if (string.IsNullOrEmpty(lines[i]))
                    continue;

                var run = CreateRun(lines[i], segment);
                inlines.Add(run);
                if (segment.Obfuscated)
                    _obfuscatedRuns.Add(run);
            }
        }

        Inlines = inlines;
        if (_obfuscatedRuns.Count > 0)
            _obfuscatedTimer.Start();
    }

    private static Run CreateRun(string text, MinecraftTextSegment segment)
    {
        var run = new Run(text);
        if (segment.ColorHex != null)
        {
            try
            {
                run.Foreground = new SolidColorBrush(Color.Parse(segment.ColorHex));
            }
            catch (FormatException)
            {
            }
        }

        if (segment.Bold)
            run.FontWeight = FontWeight.Bold;
        if (segment.Italic)
            run.FontStyle = FontStyle.Italic;
        if (segment.Underline || segment.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (segment.Underline)
                foreach (var decoration in Avalonia.Media.TextDecorations.Underline)
                    decorations.Add(decoration);
            if (segment.Strikethrough)
                foreach (var decoration in Avalonia.Media.TextDecorations.Strikethrough)
                    decorations.Add(decoration);
            run.TextDecorations = decorations;
        }

        return run;
    }

    private void TickObfuscated()
    {
        foreach (var run in _obfuscatedRuns)
        {
            if (string.IsNullOrEmpty(run.Text))
                continue;

            var chars = new char[run.Text.Length];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = ObfuscatedChars[Random.Next(ObfuscatedChars.Length)];
            run.Text = new string(chars);
        }
    }
}
