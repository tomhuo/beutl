﻿using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.Commands;
using BeUtl.ProjectSystem;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Styling;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public sealed partial class EditorBadge : UserControl
{
    public EditorBadge()
    {
        InitializeComponent();
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void EditAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && viewModel.WrappedProperty.Tag is IAnimatablePropertyInstance setter)
        {
            EditView editView = this.FindLogicalAncestorOfType<EditView>();
            if (editView.DataContext is EditViewModel editViewModel)
            {
                Layer? layer = setter.FindRequiredLogicalParent<Layer>();
                AnimationTimelineViewModel? anmViewModel =
                    editViewModel.BottomTabItems.OfType<AnimationTimelineViewModel>()
                        .FirstOrDefault(anmtl => ReferenceEquals(anmtl.Setter, setter));

                if (anmViewModel == null)
                {
                    anmViewModel = new AnimationTimelineViewModel(
                        layer,
                        setter,
                        viewModel.Description,
                        editViewModel)
                    {
                        IsSelected =
                        {
                            Value = true
                        }
                    };
                }

                editViewModel.OpenToolTab(anmViewModel);
            }
        }
    }

    private void DeleteSetter_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BaseEditorViewModel viewModel
            && this.FindLogicalAncestorOfType<StyleEditor>()?.DataContext is StyleEditorViewModel parentViewModel
            && viewModel.WrappedProperty is IStylingSetterWrapper wrapper
            && parentViewModel.Style.Value is Style style
            && wrapper.Tag is ISetter setter)
        {
            new RemoveCommand<ISetter>(style.Setters, setter).DoAndRecord(CommandRecorder.Default);
        }
    }
}
