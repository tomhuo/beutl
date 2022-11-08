﻿using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Streaming;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class RectOperator : StreamStyledSource
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<Rectangle>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<float>(Drawable.WidthProperty, 100));
        initializing.Add(new Setter<float>(Drawable.HeightProperty, 100));
        initializing.Add(new Setter<float>(Rectangle.StrokeWidthProperty, 4000));
        initializing.Add(new Setter<IBrush?>(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White)));
    }
}
