﻿using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;

using BeUtl.Collections;
using BeUtl.Framework.Service;
using BeUtl.Models.ExtensionsPages.DevelopPages;
using BeUtl.Services;

using DynamicData;

using Firebase.Storage;

using Google.Cloud.Firestore;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using SkiaSharp;

namespace BeUtl.ViewModels.ExtensionsPages.DevelopPages;

public sealed class PackageSettingsPageViewModel : IDisposable
{
    private readonly PackageController _packageController = ServiceLocator.Current.GetRequiredService<PackageController>();
    private readonly HttpClient _httpClient = ServiceLocator.Current.GetRequiredService<HttpClient>();
    private readonly CompositeDisposable _disposables = new(23);
    private readonly object _lockObject = new();
    private readonly FirestoreChangeListener _listener;
    private readonly WeakReference<PackageDetailsPageViewModel?> _parentWeak;

    public PackageSettingsPageViewModel(DocumentReference docRef, PackageDetailsPageViewModel parent)
    {
        Reference = docRef;
        _parentWeak = new WeakReference<PackageDetailsPageViewModel?>(parent);

        // 入力用プロパティの作成（オリジナルが変更されたら同期する）
        Name = parent.Name.ToReactiveProperty("").DisposeWith(_disposables);
        DisplayName = parent.DisplayName.ToReactiveProperty("").DisposeWith(_disposables);
        Description = parent.Description.ToReactiveProperty("").DisposeWith(_disposables);
        ShortDescription = parent.ShortDescription.ToReactiveProperty("").DisposeWith(_disposables);
        LogoImageId = parent.LogoId.ToReactiveProperty()
            .DisposeWith(_disposables);

        // ロゴ画像
        // 1. IDからストレージへの参照を作成
        LogoStream = LogoImageId
            .Do(_ => IsLogoLoading.Value = true)
            .SelectMany(id => _packageController.GetPackageImageStream(Reference.Id, id))
            .Do(_ => IsLogoLoading.Value = false)
            .DisposePreviousValue()
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        LogoImage = LogoStream
            .Select(st => st != null ? new Bitmap(st) : null)
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim(null)
            .DisposeWith(_disposables);

        ScreenshotsArray = parent.Screenshots.ToReactiveProperty(Array.Empty<string>())
            .DisposeWith(_disposables);
        ScreenshotsArray.Subscribe(async array =>
        {
            IsScreenshotLoading.Value = true;
            var list = new List<ImageModel>(array.Length);
            foreach (string item in array)
            {
                ImageModel? exits = Screenshots.FirstOrDefault(i => i.Name == item);

                if (exits != null)
                {
                    list.Add(exits);
                }
                else
                {
                    MemoryStream? stream = await _packageController.GetPackageImageStream(Reference.Id, item);
                    if (stream != null)
                    {
                        var bitmap = new Bitmap(stream);
                        list.Add(new ImageModel(stream, bitmap, item));
                    }
                }
            }

            ImageModel[] excepted = Screenshots.Except(list).ToArray();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Screenshots.Clear();
                Screenshots.AddRange(list);
            });
            IsScreenshotLoading.Value = false;

            foreach (ImageModel item in excepted)
            {
                item.Stream.Dispose();
                item.Bitmap.Dispose();
            }
        }).DisposeWith(_disposables);

        // 値が変更されるか
        IsChanging = Name.CombineLatest(parent.Name).Select(t => t.First == t.Second)
            .CombineLatest(
                DisplayName.CombineLatest(parent.DisplayName).Select(t => t.First == t.Second),
                Description.CombineLatest(parent.Description).Select(t => t.First == t.Second),
                ShortDescription.CombineLatest(parent.ShortDescription).Select(t => t.First == t.Second),
                LogoNoChanged,
                ScreenshotsArray.CombineLatest(Screenshots.ToCollectionChanged<ImageModel>().Select(_ => Screenshots).Publish(Screenshots).RefCount())
                    .Select(t => t.First.SequenceEqual(t.Second.Select(i => i.Name))))
            .Select(t => !(t.First && t.Second && t.Third && t.Fourth && t.Fifth && t.Sixth))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // データ検証を設定
        Name.SetValidateNotifyError(NotNullOrWhitespace);
        DisplayName.SetValidateNotifyError(NotNullOrWhitespace);
        Description.SetValidateNotifyError(NotNullOrWhitespace);
        ShortDescription.SetValidateNotifyError(NotNullOrWhitespace);

        // コマンドを初期化
        // Todo: IsLogoLoading, IsScreenshotLoadingの場合、実行できないようにする。
        Save = new AsyncReactiveCommand(Name.ObserveHasErrors
            .CombineLatest(
                DisplayName.ObserveHasErrors,
                Description.ObserveHasErrors,
                ShortDescription.ObserveHasErrors)
            .Select(t => !(t.First || t.Second || t.Third || t.Fourth)))
            .DisposeWith(_disposables);
        Save.Subscribe(async () =>
        {
            var dict = new Dictionary<string, object>
            {
                ["name"] = Name.Value,
                ["displayName"] = DisplayName.Value,
                ["description"] = Description.Value,
                ["shortDescription"] = ShortDescription.Value,
                ["visible"] = Parent.IsPublic.Value
            };

            if (LogoStream.Value?.CanRead == true && !LogoNoChanged.Value)
            {
                string? newName = Guid.NewGuid().ToString();
                dict["logo"] = newName;
                LogoStream.Value.Position = 0;
                await _packageController.GetPackageImageRef(Reference.Id, newName)
                    .PutAsync(LogoStream.Value, default, "image/jpeg");

                if (Parent.LogoId.Value is string oldName && Parent.LogoImage.Value != null)
                {
                    await _packageController.GetPackageImageRef(Reference.Id, oldName)
                        .DeleteAsync();
                }
            }
            else if (Parent.LogoId.Value != null)
            {
                dict["logo"] = Parent.LogoId.Value;
            }

            LogoNoChanged.Value = true;

            string[] oldScreenshots = ScreenshotsArray.Value;
            ImageModel[] newScreenshots = Screenshots.ToArray();

            if (Screenshots.Count > 0)
            {
                dict["screenshots"] = newScreenshots.Select(i => i.Name).ToArray();

                // 作成
                foreach (ImageModel item in newScreenshots.ExceptBy(oldScreenshots, i => i.Name))
                {
                    item.Stream.Position = 0;
                    await _packageController.GetPackageImageRef(Reference.Id, item.Name)
                        .PutAsync(item.Stream, default, "image/jpeg");
                }
            }

            await Reference.SetAsync(dict, SetOptions.Overwrite);

            // 削除
            foreach (string item in oldScreenshots.Except(newScreenshots.Select(i => i.Name)))
            {
                await _packageController.GetPackageImageRef(Reference.Id, item)
                    .DeleteAsync();
            }
        }).DisposeWith(_disposables);

        DiscardChanges.Subscribe(async () =>
        {
            DocumentSnapshot snapshot = await Reference.GetSnapshotAsync();
            Name.Value = snapshot.GetValue<string>("name");
            DisplayName.Value = snapshot.GetValue<string>("displayName");
            Description.Value = snapshot.GetValue<string>("description");
            ShortDescription.Value = snapshot.GetValue<string>("shortDescription");
            LogoImageId.ForceNotify();
            LogoNoChanged.Value = true;

            ScreenshotsArray.ForceNotify();
        }).DisposeWith(_disposables);

        Delete.Subscribe(async () =>
        {
            // Todo: コレクションやストレージのファイルを削除する
            await Reference.DeleteAsync();
        }).DisposeWith(_disposables);

        MakePublic.Subscribe(async () => await Reference.UpdateAsync("visible", true)).DisposeWith(_disposables);

        MakePrivate.Subscribe(async () => await Reference.UpdateAsync("visible", false)).DisposeWith(_disposables);

        SetLogo.Subscribe(file =>
        {
            if (File.Exists(file))
            {
                const int SIZE = 400;
                var dstBmp = new SKBitmap(SIZE, SIZE, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using (var srcBmp = SKBitmap.Decode(file))
                using (var canvas = new SKCanvas(dstBmp))
                {
                    float x = SIZE / (float)srcBmp.Width;
                    float y = SIZE / (float)srcBmp.Height;
                    float w = srcBmp.Width * MathF.Max(x, y);
                    float h = srcBmp.Height * MathF.Max(x, y);
                    Rect rect = new Rect(0, 0, SIZE, SIZE)
                        .CenterRect(new Rect(0, 0, w, h));
                    canvas.DrawBitmap(srcBmp, rect.ToSKRect());
                    canvas.Flush();
                }

                var stream = new MemoryStream();
                dstBmp.Encode(stream, SKEncodedImageFormat.Jpeg, 100);
                dstBmp.Dispose();
                stream.Position = 0;
                LogoStream.Value = stream;

                LogoNoChanged.Value = false;
            }
        }).DisposeWith(_disposables);

        CanAddScreenshot = Screenshots.ObserveProperty(i => i.Count)
            .Select(c => c < 4)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        AddScreenshot = new ReactiveCommand<string>(CanAddScreenshot)
            .DisposeWith(_disposables);
        AddScreenshot.Subscribe(file =>
        {
            if (File.Exists(file))
            {
                const int SIZE = 800;

                using (var srcBmp = SKBitmap.Decode(file))
                {
                    float x = SIZE / (float)srcBmp.Width;
                    float y = SIZE / (float)srcBmp.Height;
                    float w = srcBmp.Width * MathF.Max(x, y);
                    float h = srcBmp.Height * MathF.Max(x, y);
                    SKBitmap dstBmp = srcBmp.Resize(new SKImageInfo((int)w, (int)h, SKColorType.Bgra8888, SKAlphaType.Opaque), SKFilterQuality.Medium);
                    var stream = new MemoryStream();
                    dstBmp.Encode(stream, SKEncodedImageFormat.Jpeg, 100);
                    dstBmp.Dispose();
                    stream.Position = 0;

                    Screenshots.Add(new ImageModel(stream, new Bitmap(stream), Guid.NewGuid().ToString()));
                }
            }
        }).DisposeWith(_disposables);

        MoveScreenshotFront.Subscribe(item =>
        {
            int idx = Screenshots.IndexOf(item);
            if (idx == 0)
            {
                Screenshots.Move(idx, Screenshots.Count - 1);
            }
            else
            {
                Screenshots.Move(idx, idx - 1);
            }
        }).DisposeWith(_disposables);

        MoveScreenshotBack.Subscribe(item =>
        {
            int idx = Screenshots.IndexOf(item);
            if (idx == Screenshots.Count - 1)
            {
                Screenshots.Move(idx, 0);
            }
            else
            {
                Screenshots.Move(idx, idx + 1);
            }
        }).DisposeWith(_disposables);

        DeleteScreenshot.Subscribe(item =>
        {
            Screenshots.Remove(item);
            item.Bitmap.Dispose();
            item.Stream.Dispose();
        }).DisposeWith(_disposables);

        CollectionReference resources = Parent.Reference.Collection("resources");
        resources.GetSnapshotAsync()
            .ToObservable()
            .Subscribe(snapshot =>
            {
                foreach (DocumentSnapshot item in snapshot.Documents)
                {
                    lock (_lockObject)
                    {
                        if (!Items.Any(p => p.Reference.Id == item.Reference.Id))
                        {
                            var viewModel = new ResourcePageViewModel(item.Reference, this);
                            viewModel.Update(item);
                            Items.Add(viewModel);
                        }
                    }
                }
            });

        _listener = resources.Listen(snapshot =>
        {
            foreach (DocumentChange item in snapshot.Changes)
            {
                lock (_lockObject)
                {
                    switch (item.ChangeType)
                    {
                        case DocumentChange.Type.Added when item.NewIndex.HasValue:
                            if (!Items.Any(p => p.Reference.Id == item.Document.Reference.Id))
                            {
                                var viewModel = new ResourcePageViewModel(item.Document.Reference, this);
                                viewModel.Update(item.Document);
                                Items.Add(viewModel);
                            }
                            break;
                        case DocumentChange.Type.Removed when item.OldIndex.HasValue:
                            foreach (ResourcePageViewModel viewModel in Items)
                            {
                                if (viewModel.Reference.Id == item.Document.Id)
                                {
                                    Items.Remove(viewModel);
                                    viewModel.Dispose();
                                    return;
                                }
                            }
                            break;
                        case DocumentChange.Type.Modified:
                            foreach (ResourcePageViewModel viewModel in Items)
                            {
                                if (viewModel.Reference.Id == item.Document.Id)
                                {
                                    viewModel.Update(item.Document);
                                    return;
                                }
                            }
                            break;
                    }
                }
            }
        });
    }

    ~PackageSettingsPageViewModel()
    {
        Dispose();
    }

    public DocumentReference Reference { get; }

    public PackageDetailsPageViewModel Parent
        => _parentWeak.TryGetTarget(out PackageDetailsPageViewModel? parent)
            ? parent
            : null!;

    public ReadOnlyReactivePropertySlim<bool> IsChanging { get; }

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> DisplayName { get; } = new();

    public ReactiveProperty<string> Description { get; } = new();

    public ReactiveProperty<string> ShortDescription { get; } = new();

    public ReactiveProperty<string?> LogoImageId { get; }

    public ReactiveProperty<MemoryStream?> LogoStream { get; }

    public ReactivePropertySlim<bool> LogoNoChanged { get; } = new(true);

    public ReactivePropertySlim<bool> IsLogoLoading { get; } = new(false);

    public ReadOnlyReactivePropertySlim<Bitmap?> LogoImage { get; }

    public ReactiveCommand<string> SetLogo { get; } = new();

    public ReactiveProperty<string[]> ScreenshotsArray { get; }

    public CoreList<ImageModel> Screenshots { get; } = new();

    public ReactivePropertySlim<bool> IsScreenshotLoading { get; } = new(false);

    public AsyncReactiveCommand Save { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddScreenshot { get; }

    public ReactiveCommand<string> AddScreenshot { get; }

    public ReactiveCommand<ImageModel> MoveScreenshotFront { get; } = new();

    public ReactiveCommand<ImageModel> MoveScreenshotBack { get; } = new();

    public ReactiveCommand<ImageModel> DeleteScreenshot { get; } = new();

    public AsyncReactiveCommand DiscardChanges { get; } = new();

    public ReactiveCommand Delete { get; } = new();

    public ReactiveCommand MakePublic { get; } = new();

    public ReactiveCommand MakePrivate { get; } = new();

    public CoreList<ResourcePageViewModel> Items { get; } = new();

    public void Dispose()
    {
        Debug.WriteLine($"{GetType().Name} disposed (Count: {_disposables.Count}).");

        _disposables.Dispose();

        _listener.StopAsync();

        foreach (ResourcePageViewModel item in Items.AsSpan())
        {
            item.Dispose();
        }
        Items.Clear();

        GC.SuppressFinalize(this);
    }

    private static string NotNullOrWhitespace(string str)
    {
        if (!string.IsNullOrWhiteSpace(str))
        {
            return null!;
        }
        else
        {
            return "Please enter a string.";
        }
    }
}
