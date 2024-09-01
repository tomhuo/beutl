﻿using System.ComponentModel;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Transformation;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.Operation;
using Beutl.Operators.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using LibraryService = Beutl.Services.LibraryService;

namespace Beutl.ViewModels;

public sealed class ToolTabViewModel(IToolContext context, EditViewModel editViewModel) : IDisposable
{
    public IToolContext Context { get; private set; } = context;

    public IconSource Icon { get; } = context.Extension.GetIcon();

    public EditViewModel EditViewModel { get; } = editViewModel;

    public void Dispose()
    {
        Context.Dispose();
        Context = null!;
    }
}

public sealed class EditViewModel : IEditorContext, ITimelineOptionsProvider, ISupportCloseAnimation,
    ISupportAutoSaveEditorContext
{
    private readonly ILogger _logger = Log.CreateLogger<EditViewModel>();

    private readonly CompositeDisposable _disposables = [];

    public EditViewModel(Scene scene)
    {
        Scene = scene;
        SceneId = scene.Id.ToString();
        CurrentTime = new ReactivePropertySlim<TimeSpan>()
            .DisposeWith(_disposables);
        Renderer = scene.GetObservable(Scene.FrameSizeProperty).Select(_ => new SceneRenderer(Scene))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;
        Composer = Renderer.Select(v => new SceneComposer(Scene, v))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;

        FrameCacheManager = scene.GetObservable(Scene.FrameSizeProperty)
            .Select(v => new FrameCacheManager(v, CreateFrameCacheOptions()) { IsEnabled = config.IsFrameCacheEnabled })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables)!;

        config.PropertyChanged += OnEditorConfigPropertyChanged;

        SelectedObject = new ReactiveProperty<CoreObject?>()
            .DisposeWith(_disposables);

        Scale = Options.Select(o => o.Scale);
        Offset = Options.Select(o => o.Offset);
        SelectedObject.CombineWithPrevious()
            .Subscribe(v =>
            {
                if (v.OldValue is IHierarchical oldHierarchical)
                    oldHierarchical.DetachedFromHierarchy -= OnSelectedObjectDetachedFromHierarchy;

                if (v.NewValue is IHierarchical newHierarchical)
                    newHierarchical.DetachedFromHierarchy += OnSelectedObjectDetachedFromHierarchy;
            })
            .DisposeWith(_disposables);

        SelectedLayerNumber = SelectedObject.Select(v =>
                (v as Element)?.GetObservable(Element.ZIndexProperty).Select(i => (int?)i) ??
                Observable.Return<int?>(null))
            .Switch()
            .ToReadOnlyReactivePropertySlim();

        Player = new PlayerViewModel(this)
            .DisposeWith(_disposables);
        Commands = new KnownCommandsImpl(scene, this);
        CommandRecorder = new CommandRecorder();
        BufferStatus = new BufferStatusViewModel(this)
            .DisposeWith(_disposables);

        KeyBindings = CreateKeyBindings();

        CommandRecorder.Executed += OnCommandRecorderExecuted;

        void ConfigureToolsList(ReactiveCollection<ToolTabViewModel> list,
            ReactiveProperty<ToolTabViewModel?> selected)
        {
            var disposables = new List<(ToolTabViewModel, IDisposable)>();

            selected.Subscribe(x =>
                list.ToObservable()
                    .Where(y => y != x && y.Context.DisplayMode.Value == ToolTabExtension.TabDisplayMode.Docked)
                    .Subscribe(y => y.Context.IsSelected.Value = false));

            list.ObserveAddChanged()
                .Subscribe(x =>
                {
                    var disposable = x.Context.IsSelected.Subscribe(w =>
                    {
                        if (w && x.Context.DisplayMode.Value == ToolTabExtension.TabDisplayMode.Docked)
                        {
                            selected.Value = x;
                        }
                        else
                        {
                            selected.Value = list.FirstOrDefault(xx =>
                                xx.Context.IsSelected.Value && xx.Context.DisplayMode.Value ==
                                ToolTabExtension.TabDisplayMode.Docked);
                        }
                    });
                    disposables.Add((x, disposable));
                });

            list.ObserveRemoveChanged()
                .Subscribe(i =>
                {
                    int index = disposables.FindIndex(x => x.Item1 == i);
                    if (0 > index) return;

                    disposables[index].Item2.Dispose();
                    disposables.RemoveAt(index);
                });
        }

        ConfigureToolsList(LeftTopTools, SelectedLeftTopTool);
        ConfigureToolsList(LeftTools, SelectedLeftTool);
        ConfigureToolsList(LeftBottomTools, SelectedLeftBottomTool);
        ConfigureToolsList(RightTopTools, SelectedRightTopTool);
        ConfigureToolsList(RightTools, SelectedRightTool);
        ConfigureToolsList(RightBottomTools, SelectedRightBottomTool);

        RestoreState();
    }

    private static FrameCacheOptions CreateFrameCacheOptions()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        return new FrameCacheOptions(Scale: (FrameCacheScale)config.FrameCacheScale,
            ColorType: (FrameCacheColorType)config.FrameCacheColorType);
    }

    private void OnEditorConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is EditorConfig config)
        {
            if (e.PropertyName is nameof(EditorConfig.FrameCacheColorType) or nameof(EditorConfig.FrameCacheScale))
            {
                FrameCacheManager.Value.Options = FrameCacheManager.Value.Options with
                {
                    ColorType = (FrameCacheColorType)config.FrameCacheColorType,
                    Scale = (FrameCacheScale)config.FrameCacheScale
                };
            }
            else if (e.PropertyName is nameof(EditorConfig.IsFrameCacheEnabled))
            {
                FrameCacheManager.Value.IsEnabled = config.IsFrameCacheEnabled;
                if (!config.IsFrameCacheEnabled)
                {
                    FrameCacheManager.Value.Clear();
                }
            }
            else if (e.PropertyName is nameof(EditorConfig.IsNodeCacheEnabled)
                     or nameof(EditorConfig.NodeCacheMaxPixels)
                     or nameof(EditorConfig.NodeCacheMinPixels))
            {
                RenderCacheContext? cacheContext = Renderer.Value.GetCacheContext();
                if (cacheContext != null)
                {
                    cacheContext.CacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();
                }
            }
        }
    }

    private void OnCommandRecorderExecuted(object? sender, CommandExecutedEventArgs e)
    {
        Task.Run(() =>
        {
            int rate = Player.GetFrameRate();
            IEnumerable<TimeRange> affectedRange = e.Command is IAffectsTimelineCommand affectsTimeline
                ? affectsTimeline.GetAffectedRange()
                : e.Storables.OfType<Element>().Select(v => v.Range);

            FrameCacheManager.Value.DeleteAndUpdateBlocks(affectedRange
                .Select(item => (Start: (int)item.Start.ToFrameNumber(rate),
                    End: (int)Math.Ceiling(item.End.ToFrameNumber(rate)))));
        });

        if (GlobalConfiguration.Instance.EditorConfig.IsAutoSaveEnabled)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                foreach (IStorable item in e.Storables)
                {
                    try
                    {
                        item.Save(item.FileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An exception occurred while saving the file.");
                        NotificationService.ShowError(string.Empty,
                            Message.An_exception_occurred_while_saving_the_file);
                    }
                }

                try
                {
                    SaveState();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred while saving the view state.");
                }
            });
        }
    }

    private void OnSelectedObjectDetachedFromHierarchy(object? sender, HierarchyAttachmentEventArgs e)
    {
        SelectedObject.Value = null;
    }

    // Telemetryで使う
    public string SceneId { get; }

    public Scene Scene { get; private set; }

    public ReactivePropertySlim<TimeSpan> CurrentTime { get; }

    public ReadOnlyReactivePropertySlim<SceneRenderer> Renderer { get; }

    public ReadOnlyReactivePropertySlim<SceneComposer> Composer { get; }

    public ReactiveCollection<ToolTabViewModel> LeftTopTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedLeftTopTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> LeftTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedLeftTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> LeftBottomTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedLeftBottomTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> RightTopTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedRightTopTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> RightTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedRightTool { get; } = new();

    public ReactiveCollection<ToolTabViewModel> RightBottomTools { get; } = [];

    public ReactiveProperty<ToolTabViewModel?> SelectedRightBottomTool { get; } = new();

    public ReactiveProperty<CoreObject?> SelectedObject { get; }

    public ReactivePropertySlim<bool> IsEnabled { get; } = new(true);

    public ReadOnlyReactivePropertySlim<int?> SelectedLayerNumber { get; }

    public PlayerViewModel Player { get; private set; }

    public BufferStatusViewModel BufferStatus { get; private set; }

    public CommandRecorder CommandRecorder { get; private set; }

    public ReadOnlyReactivePropertySlim<FrameCacheManager> FrameCacheManager { get; private set; }

    public EditorExtension Extension => SceneEditorExtension.Instance;

    public string EdittingFile => Scene.FileName;

    public IKnownEditorCommands? Commands { get; private set; }

    public IReactiveProperty<TimelineOptions> Options { get; } =
        new ReactiveProperty<TimelineOptions>(new TimelineOptions());

    public IObservable<float> Scale { get; }

    public IObservable<Vector2> Offset { get; }

    IReactiveProperty<bool> IEditorContext.IsEnabled => IsEnabled;

    public List<KeyBinding> KeyBindings { get; }

    private ReactiveCollection<ToolTabViewModel>[] GetNestedTools()
    {
        return
        [
            LeftTopTools, LeftTools, LeftBottomTools,
            RightTopTools, RightTools, RightBottomTools
        ];
    }

    private (ReactiveCollection<ToolTabViewModel> List, ToolTabExtension.TabPlacement Placement)[]
        GetNestedToolsWithPlacement()
    {
        return
        [
            (LeftTopTools, ToolTabExtension.TabPlacement.TopLeft),
            (LeftTools, ToolTabExtension.TabPlacement.Left),
            (LeftBottomTools, ToolTabExtension.TabPlacement.BottomLeft),
            (RightTopTools, ToolTabExtension.TabPlacement.TopRight),
            (RightTools, ToolTabExtension.TabPlacement.Right),
            (RightBottomTools, ToolTabExtension.TabPlacement.BottomRight)
        ];
    }

    private IEnumerable<ToolTabViewModel> GetAllTools()
    {
        return GetNestedTools().SelectMany(i => i);
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing EditViewModel ({SceneId}).", SceneId);
        GlobalConfiguration.Instance.EditorConfig.PropertyChanged -= OnEditorConfigPropertyChanged;
        SaveState();
        _disposables.Dispose();
        Options.Dispose();
        IsEnabled.Dispose();
        Player = null!;
        BufferStatus = null!;

        foreach (var tools in GetNestedTools())
        {
            foreach (ToolTabViewModel item in tools)
            {
                item.Dispose();
            }

            tools.Clear();
        }

        SelectedObject.Value = null;

        Scene = null!;
        Commands = null!;
        CommandRecorder.Executed -= OnCommandRecorderExecuted;
        CommandRecorder.Clear();
        FrameCacheManager.Value.Dispose();
        FrameCacheManager.Dispose();

        _logger.LogInformation("Disposed EditViewModel ({SceneId}).", SceneId);
    }

    public T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext
    {
        return GetAllTools()
            .Select(i => i.Context)
            .OfType<T>()
            .FirstOrDefault(condition);
    }

    public T? FindToolTab<T>()
        where T : IToolContext
    {
        return FindToolTab<T>(_ => true);
    }

    public bool OpenToolTab(IToolContext item)
    {
        _logger.LogInformation("'{ToolTabName}' has been opened. ({SceneId})", item.Extension.Name, SceneId);
        try
        {
            var tools = GetAllTools();
            // ReSharper disable PossibleMultipleEnumeration
            if (tools.Any(x => x.Context == item))
            {
                item.IsSelected.Value = true;
                return true;
            }
            else if (!item.Extension.CanMultiple
                     && tools.Any(x => x.Context.Extension == item.Extension))
            {
                return false;
            }
            else
            {
                ReactiveCollection<ToolTabViewModel> list = item.Placement.Value switch
                {
                    ToolTabExtension.TabPlacement.Right => RightTools,
                    ToolTabExtension.TabPlacement.Left => LeftTools,
                    ToolTabExtension.TabPlacement.TopRight => RightTopTools,
                    ToolTabExtension.TabPlacement.BottomRight => RightBottomTools,
                    ToolTabExtension.TabPlacement.TopLeft => LeftTools,
#pragma warning disable CS0618 // 型またはメンバーが旧型式です
                    ToolTabExtension.TabPlacement.BottomLeft or ToolTabExtension.TabPlacement.Bottom => LeftBottomTools,
#pragma warning restore CS0618 // 型またはメンバーが旧型式です
                    _ => RightTools
                };
                item.IsSelected.Value = true;
                list.Add(new ToolTabViewModel(item, this));
                return true;
            }
            // ReSharper restore PossibleMultipleEnumeration
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to OpenToolTab.");
            return false;
        }
    }

    public void CloseToolTab(IToolContext item)
    {
        _logger.LogInformation("CloseToolTab {ToolName}", item.Extension.Name);
        try
        {
            foreach (ReactiveCollection<ToolTabViewModel> tools in GetNestedTools())
            {
                if (tools.FirstOrDefault(x => x.Context == item) is { } found)
                {
                    tools.Remove(found);
                    break;
                }
            }

            item.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to CloseToolTab.");
        }
    }

    private string ViewStateDirectory()
    {
        string directory = Path.GetDirectoryName(EdittingFile)!;

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState()
    {
        string viewStateDir = ViewStateDirectory();
        var json = new JsonObject
        {
            ["selected-object"] = SelectedObject.Value?.Id,
            ["max-layer-count"] = Options.Value.MaxLayerCount,
            ["scale"] = Options.Value.Scale,
            ["offset"] = new JsonObject { ["x"] = Options.Value.Offset.X, ["y"] = Options.Value.Offset.Y, }
        };

        foreach (var (list, placement) in GetNestedToolsWithPlacement())
        {
            var jsonObject = new JsonObject();
            var jsonArray = new JsonArray();
            int selectedIndex = 0;

            foreach (ToolTabViewModel? item in list)
            {
                var itemJson = new JsonObject();
                item.Context.WriteToJson(itemJson);

                itemJson.WriteDiscriminator(item.Context.Extension.GetType());
                jsonArray.Add(itemJson);

                if (item.Context.IsSelected.Value &&
                    item.Context.DisplayMode.Value == ToolTabExtension.TabDisplayMode.Docked)
                {
                    jsonObject["SelectedIndex"] = selectedIndex;
                }
                else
                {
                    selectedIndex++;
                }
            }

            jsonObject["Items"] = jsonArray;
            json[placement.ToString()] = jsonObject;
        }

        json["current-time"] = JsonValue.Create(CurrentTime.Value);

        json.JsonSave(Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(EdittingFile)}.config"));
    }

    private void RestoreState()
    {
        string viewStateDir = ViewStateDirectory();
        string viewStateFile = Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(EdittingFile)}.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is not JsonObject jsonObject)
                return;

            try
            {
                Guid? id = (Guid?)json["selected-object"];
                if (id.HasValue)
                {
                    var searcher = new ObjectSearcher(Scene, o => o is CoreObject obj && obj.Id == id.Value);
                    SelectedObject.Value = searcher.Search() as CoreObject;
                }
            }
            catch
            {
            }

            var timelineOptions = new TimelineOptions();

            if (jsonObject.TryGetPropertyValue("max-layer-count", out JsonNode? maxLayer)
                && maxLayer is JsonValue maxLayerValue
                && maxLayerValue.TryGetValue(out int maxLayerCount))
            {
                timelineOptions = timelineOptions with { MaxLayerCount = maxLayerCount };
            }

            if (jsonObject.TryGetPropertyValue("scale", out JsonNode? scaleNode)
                && scaleNode is JsonValue scaleValue
                && scaleValue.TryGetValue(out float scale))
            {
                timelineOptions = timelineOptions with { Scale = scale };
            }

            if (jsonObject.TryGetPropertyValue("offset", out JsonNode? offsetNode)
                && offsetNode is JsonObject offsetObj
                && offsetObj.TryGetPropertyValue("x", out JsonNode? xNode)
                && offsetObj.TryGetPropertyValue("y", out JsonNode? yNode)
                && xNode is JsonValue xValue
                && yNode is JsonValue yValue
                && xValue.TryGetValue(out float x)
                && yValue.TryGetValue(out float y))
            {
                timelineOptions = timelineOptions with { Offset = new Vector2(x, y) };
            }

            Options.Value = timelineOptions;

            void RestoreTabItems(JsonArray source, ReactiveCollection<ToolTabViewModel> destination)
            {
                destination.Clear();
                foreach (JsonNode? item in source)
                {
                    if (item is JsonObject itemObject
                        && itemObject.TryGetDiscriminator(out Type? type)
                        && ExtensionProvider.Current.AllExtensions.FirstOrDefault(x => x.GetType() == type) is
                            ToolTabExtension extension
                        && extension.TryCreateContext(this, out IToolContext? context))
                    {
                        context.ReadFromJson(itemObject);
                        destination.Add(new ToolTabViewModel(context, this));
                    }
                }
            }

            foreach (var (list, placement) in GetNestedToolsWithPlacement())
            {
                if (!jsonObject.TryGetPropertyValue(placement.ToString(), out JsonNode? node)) continue;
                if (node is not JsonObject tabObject) continue;
                if (!tabObject.TryGetPropertyValue("Items", out JsonNode? itemsNode)) continue;
                if (itemsNode is not JsonArray listItems) continue;

                RestoreTabItems(listItems, list);

                if (tabObject.TryGetPropertyValueAsJsonValue("SelectedIndex", out int index)
                    && 0 <= index && index < list.Count)
                {
                    list[index].Context.IsSelected.Value = true;
                }
            }

            if (jsonObject.TryGetPropertyValueAsJsonValue("current-time", out string? currentTimeStr)
                && TimeSpan.TryParse(currentTimeStr, out TimeSpan currentTime))
            {
                CurrentTime.Value = currentTime;
            }
        }
        else
        {
            if (TimelineTabExtension.Instance.TryCreateContext(this, out IToolContext? tab1))
            {
                OpenToolTab(tab1);
            }

            if (SourceOperatorsTabExtension.Instance.TryCreateContext(this, out IToolContext? tab2))
            {
                OpenToolTab(tab2);
            }

            if (LibraryTabExtension.Instance.TryCreateContext(this, out IToolContext? tab3))
            {
                OpenToolTab(tab3);
            }
        }
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Scene))
            return Scene;

        if (serviceType.IsAssignableTo(typeof(IEditorContext)))
            return this;

        if (serviceType.IsAssignableTo(typeof(ISupportCloseAnimation)))
            return this;

        if (serviceType == typeof(CommandRecorder))
            return CommandRecorder;

        return null;
    }

    public void AddElement(ElementDescription desc)
    {
        Element CreateElement()
        {
            return new Element()
            {
                Start = desc.Start,
                Length = desc.Length,
                ZIndex = desc.Layer,
                FileName = RandomFileNameGenerator.Generate(Path.GetDirectoryName(Scene.FileName)!,
                    Constants.ElementFileExtension)
            };
        }

        void SetAccentColor(Element element, string str)
        {
            element.AccentColor = ColorGenerator.GenerateColor(str);
        }

        void SetTransform(SourceOperation operation, SourceOperator op)
        {
            if (!desc.Position.IsDefault)
            {
                if (op.Properties.FirstOrDefault(v => v.PropertyType == typeof(ITransform)) is
                    IPropertyAdapter<ITransform?> transformp)
                {
                    ITransform? transform = transformp.GetValue();
                    AddOrSetHelper.AddOrSet(
                        ref transform,
                        new TranslateTransform(desc.Position),
                        [operation.FindHierarchicalParent<IStorable>()],
                        CommandRecorder);
                    transformp.SetValue(transform);
                }
                else
                {
                    _logger.LogWarning("The operator does not have a transform property.");
                }
            }
        }

        TimelineViewModel? timeline = FindToolTab<TimelineViewModel>();

        if (desc.FileName != null)
        {
            (TimeRange Range, int ZIndex)? scrollPos = null;

            Element CreateElementFor<T>(out T t)
                where T : SourceOperator, new()
            {
                Element element = CreateElement();
                element.Name = Path.GetFileName(desc.FileName);
                SetAccentColor(element, typeof(T).FullName!);

                element.Operation.AddChild(t = new T()).Do();
                SetTransform(element.Operation, t);

                return element;
            }

            var list = new List<IRecordableCommand>();
            if (MatchFileImage(desc.FileName))
            {
                Element element = CreateElementFor(out SourceImageOperator t);
                BitmapSource.TryOpen(desc.FileName, out BitmapSource? image);
                t.Source.Value = image;

                element.Save(element.FileName);
                list.Add(Scene.AddChild(element));
                scrollPos = (element.Range, element.ZIndex);
            }
            else if (MatchFileVideoOnly(desc.FileName))
            {
                Element element1 = CreateElementFor(out SourceVideoOperator t1);
                Element element2 = CreateElementFor(out SourceSoundOperator t2);
                element2.ZIndex++;
                VideoSource.TryOpen(desc.FileName, out VideoSource? video);
                SoundSource.TryOpen(desc.FileName, out SoundSource? sound);
                t1.Source.Value = video;
                t2.Source.Value = sound;

                if (video != null)
                    element1.Length = video.Duration;
                if (sound != null)
                    element2.Length = sound.Duration;

                element1.Save(element1.FileName);
                element2.Save(element2.FileName);
                list.Add(Scene.AddChild(element1));
                list.Add(Scene.AddChild(element2));
                scrollPos = (element1.Range, element1.ZIndex);
            }
            else if (MatchFileAudioOnly(desc.FileName))
            {
                Element element = CreateElementFor(out SourceSoundOperator t);
                SoundSource.TryOpen(desc.FileName, out SoundSource? sound);
                t.Source.Value = sound;
                if (sound != null)
                {
                    element.Length = sound.Duration;
                }

                element.Save(element.FileName);
                list.Add(Scene.AddChild(element));
                scrollPos = (element.Range, element.ZIndex);
            }

            list.ToArray()
                .ToCommand()
                .DoAndRecord(CommandRecorder);

            if (scrollPos.HasValue && timeline != null)
            {
                timeline?.ScrollTo.Execute(scrollPos.Value);
            }
        }
        else
        {
            Element element = CreateElement();
            if (desc.InitialOperator != null)
            {
                LibraryItem? item = LibraryService.Current.FindItem(desc.InitialOperator);
                if (item != null)
                {
                    element.Name = item.DisplayName;
                }

                //Todo: レイヤーのアクセントカラー
                //sLayer.AccentColor = item.InitialOperator.AccentColor;
                element.AccentColor =
                    ColorGenerator.GenerateColor(desc.InitialOperator.FullName ?? desc.InitialOperator.Name);
                var operatour = (SourceOperator)Activator.CreateInstance(desc.InitialOperator)!;
                element.Operation.AddChild(operatour).Do();
                SetTransform(element.Operation, operatour);
            }

            element.Save(element.FileName);
            Scene.AddChild(element).DoAndRecord(CommandRecorder);

            timeline?.ScrollTo.Execute((element.Range, element.ZIndex));
        }
    }

    private static bool MatchFileExtensions(string filePath, IEnumerable<string> extensions)
    {
        return extensions
            .Select(x =>
            {
                int idx = x.LastIndexOf('.');
                if (0 <= idx)
                    return x.Substring(idx);
                else
                    return x;
            })
            .Any(filePath.EndsWith);
    }

    private static bool MatchFileAudioOnly(string filePath)
    {
        return MatchFileExtensions(filePath, DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.AudioExtensions())
            .Distinct());
    }

    private static bool MatchFileVideoOnly(string filePath)
    {
        return MatchFileExtensions(filePath, DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.VideoExtensions())
            .Distinct());
    }

    private static bool MatchFileImage(string filePath)
    {
        string[] extensions =
        [
            "*.bmp",
            "*.gif",
            "*.ico",
            "*.jpg",
            "*.jpeg",
            "*.png",
            "*.wbmp",
            "*.webp",
            "*.pkm",
            "*.ktx",
            "*.astc",
            "*.dng",
            "*.heif",
            "*.avif",
        ];
        return MatchFileExtensions(filePath, extensions);
    }

    void ISupportCloseAnimation.Close(object obj)
    {
        var searcher = new ObjectSearcher(obj, v => v is IAnimation);

        IAnimation[] animations = searcher.SearchAll().OfType<IAnimation>().ToArray();
        TimelineViewModel? timeline = FindToolTab<TimelineViewModel>();
        // Timelineのインライン表示を削除
        if (timeline != null)
        {
            foreach (InlineAnimationLayerViewModel? item in timeline.Inlines
                         .IntersectBy(animations, v => v.Property.Animation)
                         .ToArray())
            {
                timeline.DetachInline(item);
            }
        }

        // BottomTabItemsから削除する
        foreach (var list in GetNestedTools())
        {
            for (int index = list.Count - 1; index >= 0; index--)
            {
                ToolTabViewModel item = list[index];
                if (item.Context is not GraphEditorTabViewModel graph) continue;

                for (int i = graph.Items.Count - 1; i >= 0; i--)
                {
                    var animation = graph.Items[i];
                    if (animations.Contains(animation.Object))
                    {
                        graph.Items.Remove(animation);
                    }
                }

                if (graph.Items.Count == 0)
                {
                    list.Remove(item);
                    item.Dispose();
                }
            }
        }
    }

    // Todo: 設定からショートカットを変更できるようにする。
    private List<KeyBinding> CreateKeyBindings()
    {
        static KeyBinding KeyBinding(Key key, KeyModifiers modifiers, ICommand command)
        {
            return new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = command };
        }

        return
        [
            // PlayPause: Space
            KeyBinding(Key.Space, KeyModifiers.None, Player.PlayPause),
            // Next: Right
            KeyBinding(Key.Right, KeyModifiers.None, Player.Next),
            // Previous: Left
            KeyBinding(Key.Left, KeyModifiers.None, Player.Previous),
            // Start: Home
            KeyBinding(Key.Home, KeyModifiers.None, Player.Start),
            // End: End
            KeyBinding(Key.End, KeyModifiers.None, Player.End),
        ];
    }

    private sealed class KnownCommandsImpl(Scene scene, EditViewModel viewModel) : IKnownEditorCommands
    {
        public ValueTask<bool> OnSave()
        {
            scene.Save(scene.FileName);
            Parallel.ForEach(scene.Children, item => item.Save(item.FileName));
            viewModel.SaveState();

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> OnUndo()
        {
            viewModel.CommandRecorder.Undo();

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> OnRedo()
        {
            viewModel.CommandRecorder.Redo();

            return ValueTask.FromResult(true);
        }
    }
}
