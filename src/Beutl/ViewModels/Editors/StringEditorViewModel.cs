﻿using Beutl.Framework;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class StringEditorViewModel : BaseEditorViewModel<string>
{
    public StringEditorViewModel(IAbstractProperty<string> property)
        : base(property)
    {
        Value = property.GetObservable()
            .Select(x => x ?? string.Empty)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables)!;
    }

    public ReadOnlyReactivePropertySlim<string> Value { get; }
}
