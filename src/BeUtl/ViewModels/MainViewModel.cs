﻿
using Avalonia;
using Avalonia.Collections;
using Avalonia.Threading;

using BeUtl.Configuration;
using BeUtl.Framework;
using BeUtl.Framework.Service;
using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;
using BeUtl.Services;
using BeUtl.Services.PrimitiveImpls;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public sealed class MainViewModel
{
    private readonly IProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly INotificationService _notificationService;
    private readonly PageExtension[] _primitivePageExtensions;
    internal readonly Task _packageLoadTask;

    public sealed class NavItemViewModel
    {
        public NavItemViewModel(PageExtension extension)
        {
            Extension = extension;
            Context = Activator.CreateInstance(extension.Context) ?? throw new Exception("コンテキストを作成できませんでした。");
            Header = extension.Header
                .Select(o => o ?? string.Empty)
                .ToReadOnlyReactivePropertySlim(string.Empty);
        }

        public NavItemViewModel(PageExtension extension, object context)
        {
            Extension = extension;
            Context = context;
            Header = extension.Header
                .Select(o => o ?? string.Empty)
                .ToReadOnlyReactivePropertySlim(string.Empty);
        }

        public ReadOnlyReactivePropertySlim<string> Header { get; }

        public PageExtension Extension { get; }

        public object Context { get; }
    }

    public MainViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();
        _editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        _notificationService = ServiceLocator.Current.GetRequiredService<INotificationService>();
        _primitivePageExtensions = new PageExtension[]
        {
            EditPageExtension.Instance,
            ExtensionsPageExtension.Instance,
            OutputPageExtension.Instance,
            SettingsPageExtension.Instance,
        };

        IsProjectOpened = _projectService.IsOpened;

        IObservable<bool> isProjectOpenedAndTabOpened = _projectService.IsOpened
            .CombineLatest(_editorService.SelectedTabItem)
            .Select(i => i.First && i.Second != null);

        IObservable<bool> isSceneOpened = _editorService.SelectedTabItem
            .SelectMany(i => i?.Context ?? Observable.Empty<IEditorContext?>())
            .Select(v => v is EditViewModel);

        AddToProject = new(isProjectOpenedAndTabOpened);
        RemoveFromProject = new(isProjectOpenedAndTabOpened);
        AddLayer = new(isSceneOpened);
        DeleteLayer = new(isSceneOpened);
        ExcludeLayer = new(isSceneOpened);
        CutLayer = new(isSceneOpened);
        CopyLayer = new(isSceneOpened);
        PasteLayer = new(isSceneOpened);
        CloseFile = new(_editorService.SelectedTabItem.Select(i => i != null));
        CloseProject = new(_projectService.IsOpened);
        Save = new(_projectService.IsOpened);
        SaveAll = new(_projectService.IsOpened);
        Undo = new(_projectService.IsOpened);
        Redo = new(_projectService.IsOpened);

        Save.Subscribe(async () =>
        {
            EditorTabItem? item = _editorService.SelectedTabItem.Value;
            if (item != null)
            {
                try
                {
                    bool result = await (item.Commands.Value == null ? ValueTask.FromResult(false) : item.Commands.Value.OnSave());

                    if (result)
                    {
                        _notificationService.Show(new Notification(
                            string.Empty,
                            string.Format(S.Message.ItemSaved, item.FileName),
                            NotificationType.Success));
                    }
                    else
                    {
                        _notificationService.Show(new Notification(
                            string.Empty,
                            S.Message.OperationCouldNotBeExecuted,
                            NotificationType.Information));
                    }
                }
                catch
                {
                    _notificationService.Show(new Notification(
                        string.Empty,
                        S.Message.OperationCouldNotBeExecuted,
                        NotificationType.Error));
                }
            }
        });

        SaveAll.Subscribe(async () =>
        {
            Project? project = _projectService.CurrentProject.Value;
            int itemsCount = 0;

            try
            {
                project?.Save(project.FileName);
                itemsCount++;

                foreach (EditorTabItem? item in _editorService.TabItems)
                {
                    if (item.Commands.Value != null
                        && await item.Commands.Value.OnSave())
                    {
                        itemsCount++;
                    }
                }

                _notificationService.Show(new Notification(
                    string.Empty,
                    string.Format(S.Message.ItemsSaved, itemsCount.ToString()),
                    NotificationType.Success));
            }
            catch
            {
                _notificationService.Show(new Notification(
                    string.Empty,
                    S.Message.OperationCouldNotBeExecuted,
                    NotificationType.Error));
            }
        });

        CloseFile.Subscribe(() =>
        {
            EditorTabItem? tabItem = _editorService.SelectedTabItem.Value;
            if (tabItem != null)
            {
                _editorService.CloseTabItem(
                    tabItem.FilePath.Value,
                    tabItem.TabOpenMode);
            }
        });
        CloseProject.Subscribe(() => _projectService.CloseProject());

        Undo.Subscribe(async () =>
        {
            IKnownEditorCommands? commands = _editorService.SelectedTabItem.Value?.Commands.Value;
            if (commands != null)
                await commands.OnUndo();
        });
        Redo.Subscribe(async () =>
        {
            IKnownEditorCommands? commands = _editorService.SelectedTabItem.Value?.Commands.Value;
            if (commands != null)
                await commands.OnRedo();
        });

        _packageLoadTask = Task.Run(async () =>
        {
            PackageManager manager = PackageManager.Instance;

            manager.ExtensionProvider._allExtensions.Add(Package.s_nextId++, _primitivePageExtensions);

            // NOTE: ここでSceneEditorExtensionを登録しているので、
            //       パッケージとして分離する場合ここを削除
            manager.ExtensionProvider._allExtensions.Add(Package.s_nextId++, new Extension[]
            {
                SceneEditorExtension.Instance,
                SceneWorkspaceItemExtension.Instance,
                TimelineTabExtension.Instance,
                AnimationTimelineTabExtension.Instance,
                ObjectPropertyTabExtension.Instance,
                StyleEditorTabExtension.Instance,
                StreamOperatorsTabExtension.Instance,
                EasingsTabExtension.Instance,
                AnimationTabExtension.Instance,
                PropertyEditorExtension.Instance,
                AlignmentsPropertyEditorExtension.Instance,
                DefaultPropertyNameExtension.Instance,
            });

            manager.LoadPackages(manager.GetPackageInfos());

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current != null)
                {
                    PackageManager.Instance.AttachToApplication(Application.Current);
                }
            });
        });

        Pages = new()
        {
            new(EditPageExtension.Instance),
            new(ExtensionsPageExtension.Instance),
            new(OutputPageExtension.Instance),
        };
        SelectedPage.Value = Pages[0];
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.RecentFiles.ForEachItem(
            item => RecentFileItems.Insert(0, item),
            item => RecentFileItems.Remove(item),
            () => RecentFileItems.Clear());

        viewConfig.RecentProjects.ForEachItem(
            item => RecentProjectItems.Insert(0, item),
            item => RecentProjectItems.Remove(item),
            () => RecentProjectItems.Clear());

        OpenRecentFile.Subscribe(file => _editorService.ActivateTabItem(file, TabOpenMode.YourSelf));

        OpenRecentProject.Subscribe(file =>
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
            INotificationService noticeService = ServiceLocator.Current.GetRequiredService<INotificationService>();

            if (!File.Exists(file))
            {
                noticeService.Show(new Notification(
                    Title: "",
                    Message: S.Warning.FileDoesNotExist));
            }
            else if (service.OpenProject(file) == null)
            {
                noticeService.Show(new Notification(
                    Title: "",
                    Message: S.Warning.CouldNotOpenProject));
            }
        });

        _packageLoadTask.ContinueWith(_ =>
        {
            PackageManager manager = PackageManager.Instance;
            IEnumerable<PageExtension> toAdd
                = manager.ExtensionProvider.AllExtensions.OfType<PageExtension>().Except(_primitivePageExtensions);
            Dispatcher.UIThread.InvokeAsync(() => Pages.AddRange(toAdd.Select(item => new NavItemViewModel(item))));
        });
    }

    public bool IsDebuggerAttached { get; } = Debugger.IsAttached;

    // File
    //    Create new
    //       Project
    //       File
    //    Open
    //       Project
    //       File
    //    Close
    //    Close project
    //    Save
    //    Save all
    //    Recent files
    //    Recent projects
    //    Exit
    public ReactiveCommand CreateNewProject { get; } = new();

    public ReactiveCommand CreateNew { get; } = new();

    public ReactiveCommand OpenProject { get; } = new();

    public ReactiveCommand OpenFile { get; } = new();

    public ReactiveCommand CloseFile { get; }

    public ReactiveCommand CloseProject { get; }

    public ReactiveCommand Save { get; }

    public ReactiveCommand SaveAll { get; }

    public ReactiveCommand<string> OpenRecentFile { get; } = new();

    public ReactiveCommand<string> OpenRecentProject { get; } = new();

    public CoreList<string> RecentFileItems { get; } = new();

    public CoreList<string> RecentProjectItems { get; } = new();

    public ReactiveCommand Exit { get; } = new();

    // Edit
    //    Undo
    //    Redo
    public ReactiveCommand Undo { get; }

    public ReactiveCommand Redo { get; }

    // Project
    //    Add
    //    Remove
    public ReactiveCommand AddToProject { get; }

    public ReactiveCommand RemoveFromProject { get; }

    // Scene
    //    New
    //    Settings
    //    Layer
    //       Add
    //       Delete
    //       Exclude
    //       Cut
    //       Copy
    //       Paste
    public ReactiveCommand NewScene { get; } = new();

    public ReactiveCommand AddLayer { get; }

    public ReactiveCommand DeleteLayer { get; }

    public ReactiveCommand ExcludeLayer { get; }

    public ReactiveCommand CutLayer { get; }

    public ReactiveCommand CopyLayer { get; }

    public ReactiveCommand PasteLayer { get; }

    public NavItemViewModel SettingsPage { get; } = new(SettingsPageExtension.Instance);

    public CoreList<NavItemViewModel> Pages { get; }

    public ReactiveProperty<NavItemViewModel?> SelectedPage { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }
}
