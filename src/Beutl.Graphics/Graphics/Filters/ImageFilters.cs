﻿using System.Collections;
using System.Collections.Specialized;

using Beutl.Collections;
using Beutl.Media;

namespace Beutl.Graphics.Filters;

public sealed class ImageFilters : CoreList<IImageFilter>, IAffectsRender
{
    public ImageFilters()
    {
        ResetBehavior = ResetBehavior.Remove;
        CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void AddHandlers(IList list)
        {
            foreach (IAffectsRender? item in list.OfType<IAffectsRender>())
            {
                item.Invalidated += Item_Invalidated;
            }
        }

        void RemoveHandlers(IList list)
        {
            foreach (IAffectsRender? item in list.OfType<IAffectsRender>())
            {
                item.Invalidated -= Item_Invalidated;
            }
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                AddHandlers(e.NewItems);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                RemoveHandlers(e.OldItems);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.OldItems is not null:
                AddHandlers(e.NewItems);
                RemoveHandlers(e.OldItems);
                break;
            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Reset:
            default:
                break;
        }

        RaiseInvalidated();
    }

    public event EventHandler? Invalidated;

    private void Item_Invalidated(object? sender, EventArgs e)
    {
        RaiseInvalidated();
    }

    private void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
