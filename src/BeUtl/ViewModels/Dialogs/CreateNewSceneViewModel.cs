﻿using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;

using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;
using BeUtl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Dialogs;

public sealed class CreateNewSceneViewModel
{
    private readonly Project _proj;

    public CreateNewSceneViewModel()
    {
        IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
        if (!service.IsOpened.Value)
            throw new Exception("The project has not been opened.");
        _proj = service.CurrentProject.Value!;

        Location.Value = _proj.RootDirectory;
        Name.Value = GenSceneName(Location.Value);

        Name.SetValidateNotifyError(n =>
        {
            if (Directory.Exists(Path.Combine(Location.Value, n)))
            {
                return (string?)Application.Current?.FindResource("S.Warning.ItAlreadyExists");
            }
            else
            {
                return null;
            }
        });
        Location.Subscribe(_ => Name.ForceValidate());
        Size.SetValidateNotifyError(s =>
        {
            if (s.Width <= 0 || s.Height <= 0)
            {
                return (string?)Application.Current?.FindResource("S.Warning.ValueLessThanOrEqualToZero");
            }
            else
            {
                return null;
            }
        });

        CanCreate = Name.CombineLatest(Location, Size).Select(t =>
        {
            string name = t.First;
            string location = t.Second;
            PixelSize size = t.Third;

            return !Directory.Exists(Path.Combine(location, name)) &&
                size.Width > 0 &&
                size.Height > 0;
        }).ToReadOnlyReactivePropertySlim();
        Create = new ReactiveCommand(CanCreate);
        Create.Subscribe(() =>
        {
            var scene = new Scene(Size.Value.Width, Size.Value.Height, Name.Value);
            scene.Save(Path.Combine(Location.Value, Name.Value, $"{Name.Value}.scene"));

            _proj.Items.Add(scene);
        });
    }

    public ReactiveProperty<PixelSize> Size { get; } = new(new PixelSize(1920, 1080));

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> Location { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanCreate { get; }

    public ReactiveCommand Create { get; }

    private static string GenSceneName(string location)
    {
        const string name = "Scene";
        int n = 1;

        while (Directory.Exists(Path.Combine(location, name + n)))
        {
            n++;
        }

        return name + n;
    }
}
