﻿using System.Text.Json.Nodes;

using BeUtl.Framework;
using BeUtl.Models;
using BeUtl.ProjectSystem;
using BeUtl.Services.PrimitiveImpls;
using BeUtl.Streaming;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Tools;

public sealed class StreamOperatorsTabViewModel : IToolContext
{
    private readonly IDisposable _disposable0;
    private IDisposable? _disposable1;
    private Layer? _oldLayer;

    public StreamOperatorsTabViewModel(EditViewModel editViewModel)
    {
        Layer = editViewModel.SelectedObject
            .Select(x => x as Layer)
            .ToReactiveProperty();

        Header = S.Common.StreamOperatorsObservable.ToReadOnlyReactivePropertySlim()!;

        _disposable0 = Layer.Subscribe(layer =>
        {
            if (_oldLayer != null)
            {
                SaveState(_oldLayer);
            }
            _oldLayer = layer;

            ClearItems();
            if (layer != null)
            {
                _disposable1?.Dispose();
                _disposable1 = layer.Operators.ForEachItem(
                    (idx, item) => Items.Insert(idx, new StreamOperatorViewModel(item)),
                    (idx, _) =>
                    {
                        Items[idx]?.Dispose();
                        Items.RemoveAt(idx);
                    },
                    () => ClearItems());

                RestoreState(layer);
            }
        });
    }

    public Action<StreamOperator>? RequestScroll { get; set; }

    public ReactiveProperty<Layer?> Layer { get; }

    public CoreList<StreamOperatorViewModel?> Items { get; } = new();

    public ToolTabExtension Extension => StreamOperatorsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void ScrollTo(StreamOperator obj)
    {
        RequestScroll?.Invoke(obj);
    }

    public void Dispose()
    {
        if (Layer.Value != null)
        {
            SaveState(Layer.Value);
        }
        _disposable0.Dispose();
        _disposable1?.Dispose();
        ClearItems();

        Layer.Dispose();
        Header.Dispose();
    }

    private static string ViewStateDirectory(Layer layer)
    {
        string directory = Path.GetDirectoryName(layer.FileName)!;

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState(Layer layer)
    {
        string viewStateDir = ViewStateDirectory(layer);
        var json = new JsonArray();
        foreach (StreamOperatorViewModel? item in Items.GetMarshal().Value)
        {
            json.Add(item?.SaveState());
        }

        json.JsonSave(Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(layer.FileName)}.operators.config"));
    }

    private void RestoreState(Layer layer)
    {
        string viewStateDir = ViewStateDirectory(layer);
        string viewStateFile = Path.Combine(viewStateDir, $"{Path.GetFileNameWithoutExtension(layer.FileName)}.operators.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var json = JsonNode.Parse(stream);
            if (json is JsonArray array)
            {
                foreach ((JsonNode? item, StreamOperatorViewModel? op) in array.Zip(Items))
                {
                    if (item != null && op != null)
                    {
                        op.RestoreState(item);
                    }
                }
            }
        }
    }

    private void ClearItems()
    {
        foreach (StreamOperatorViewModel? item in Items.GetMarshal().Value)
        {
            item?.Dispose();
        }
        Items.Clear();
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }
}
