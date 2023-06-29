﻿using Beutl.Graphics;
using Beutl.Graphics.Filters;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceImageOperator : DrawablePublishOperator<SourceImage>
{
    private string? _sourceName;

    public Setter<IImageSource?> Source { get; set; } = new(SourceImage.SourceProperty, null);

    public Setter<ITransform?> Transform { get; set; } = new(Drawable.TransformProperty, null);

    public Setter<IBrush?> Fill { get; set; } = new(Drawable.ForegroundProperty, new SolidColorBrush(Colors.White));

    public Setter<IImageFilter?> Filter { get; set; } = new(Drawable.FilterProperty, null);

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Source is { Value: { Name: string name } value } setter)
        {
            _sourceName = name;
            setter.Value = null;
            value.Dispose();
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (Source is { Value: null } setter
            && _sourceName != null
            && MediaSourceManager.Shared.OpenImageSource(_sourceName, out IImageSource? imageSource))
        {
            setter.Value = imageSource;
        }
    }
}
