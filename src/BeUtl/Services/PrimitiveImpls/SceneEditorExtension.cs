﻿using System.Diagnostics.CodeAnalysis;

using Avalonia.Media;

using BeUtl.Controls;
using BeUtl.Framework;
using BeUtl.Models;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;
using BeUtl.Views;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SceneEditorExtension : EditorExtension
{
    public static readonly SceneEditorExtension Instance = new();

    public override Geometry? Icon { get; } = FluentIconsRegular.Document.GetGeometry();

    public override string[] FileExtensions { get; } =
    {
        Constants.SceneFileExtension
    };

    public override IObservable<string> FileTypeName => S.Common.SceneFileObservable;

    public override string Name => "Scene editor";

    public override string DisplayName => "Scene editor";

    public override bool TryCreateEditor(string file, [NotNullWhen(true)] out IEditor? editor)
    {
        if (file.EndsWith($".{Constants.SceneFileExtension}"))
        {
            editor = new EditView();
            return true;
        }
        else
        {
            editor = null;
            return false;
        }
    }

    public override bool TryCreateContext(string file, [NotNullWhen(true)] out IEditorContext? context)
    {
        if (file.EndsWith($".{Constants.SceneFileExtension}")
            && ServiceLocator.Current.GetRequiredService<IWorkspaceItemContainer>().TryGetOrCreateItem(file, out Scene? model))
        {
            context = new EditViewModel(model);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
