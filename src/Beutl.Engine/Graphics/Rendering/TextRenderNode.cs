﻿using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class TextRenderNode(FormattedText text, IBrush? fill, IPen? pen)
    : BrushRenderNode(fill, pen)
{
    public FormattedText Text { get; private set; } = text;

    public bool Equals(FormattedText text, IBrush? fill, IPen? pen)
    {
        return Text == text
               && EqualityComparer<IBrush?>.Default.Equals(Fill, fill)
               && EqualityComparer<IPen?>.Default.Equals(Pen, pen);
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(Text.ActualBounds, canvas => canvas.DrawText(Text, Fill, Pen), HitTest)
        ];
    }

    private bool HitTest(Point point)
    {
        SKPath fill = Text.GetFillPath();
        if (Fill != null && fill.Contains(point.X, point.Y))
        {
            return true;
        }

        SKPath? stroke = Text.GetStrokePath();
        return stroke?.Contains(point.X, point.Y) == true;
    }
}
