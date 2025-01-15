using MarkdownParser;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.MarkdownView;

[ContentProperty(nameof(MarkdownText))]
public class MarkdownView : ContentView
{
    private readonly ILogger<MarkdownView>? _logger;
    private readonly VerticalStackLayout _layout;
    private static readonly Semaphore LoadingSemaphore = new (1, 1);
    private CancellationTokenSource? _loadingCts;

    public MarkdownView()
    {
        _layout = new VerticalStackLayout();
		Content = _layout;

        _logger ??= Handler?.MauiContext?.Services.GetService<ILogger<MarkdownView>>();
    }

    /// <summary>
    /// Workaround: space (lowercase) required for using xml:space="preserve" in XAML to keep linebreaks and spaces during HotReload
    /// </summary>
    public static readonly BindableProperty SpaceProperty = BindableProperty.Create(nameof(MarkdownText), typeof(string), typeof(MarkdownView), "preserve");
    public string space
    {
        get => (string)GetValue(SpaceProperty);
        set => SetValue(SpaceProperty, value);
    }

    public static readonly BindableProperty MarkdownTextProperty =
        BindableProperty.Create(nameof(MarkdownText), typeof(string), typeof(MarkdownView), string.Empty, propertyChanged: MarkdownTextPropertyChanged);

    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public static readonly BindableProperty IsLoadingMarkdownProperty =
        BindableProperty.Create(nameof(IsLoadingMarkdown), typeof(bool), typeof(MarkdownView), false);

    public bool IsLoadingMarkdown
    {
        get => (bool)GetValue(IsLoadingMarkdownProperty);
        set => SetValue(IsLoadingMarkdownProperty, value);
    }

    private static void MarkdownTextPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is MarkdownView markdownView)
        {
            markdownView.InvalidateMarkdownAsync();
        }
    }

    /// <summary>
    /// Invalidate current displayed views generated from Markdown,
    /// starting new view creation cycle by forcing one cycle at a time, restarting each time markdown is invalidated
    /// </summary>
    private async void InvalidateMarkdownAsync()
    {
        if (_loadingCts != null)
        {
            _loadingCts?.Cancel();
            return;
        }

        LoadingSemaphore.WaitOne();

        _loadingCts = new CancellationTokenSource();
        var isCanceled = false;

        try
        {
            await DisplayViewsFromMarkdownAsync(MarkdownText, _loadingCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            isCanceled = true;
        }
        finally
        {
            _loadingCts?.Dispose();
            _loadingCts = null;

            LoadingSemaphore.Release();
        }

        if (isCanceled)
        {
            InvalidateMarkdownAsync();
        }
    }

    protected async Task DisplayViewsFromMarkdownAsync(string markdownText, CancellationToken loadingToken)
    {
        if (string.IsNullOrWhiteSpace(MarkdownText))
        {
            await Dispatcher.DispatchAsync(_layout.Clear);
            IsLoadingMarkdown = false;

            return;
        }

        IsLoadingMarkdown = true;

        var uiComponentSupplier = new MauiViewSupplier();
        var markdownParser = new MarkdownParser<View>(uiComponentSupplier);

        await Dispatcher.DispatchAsync(() =>
        {
            var views = markdownParser.Parse(markdownText);
            loadingToken.ThrowIfCancellationRequested();

            _layout.Clear();
            foreach (var view in views)
            {
                loadingToken.ThrowIfCancellationRequested();
                _layout.Add(view);
            }

            _logger?.Log(LogLevel.Trace, "Markdown used > {markdownText}", markdownText);
            _logger?.Log(LogLevel.Information, "{viewCount} top-level views generated", views.Count);
        });

        IsLoadingMarkdown = false;
    }

}