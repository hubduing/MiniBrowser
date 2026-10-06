using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MiniBrowser.Models;
using MiniBrowser.Services;

namespace MiniBrowser.Views;

/// <summary>
/// Содержимое одной вкладки. WebView2 создаётся лениво при первой навигации;
/// при усыплении (Sleep) контрол освобождается и память возвращается системе.
/// </summary>
public partial class BrowserTabView : UserControl
{
    private readonly Tab _tab;
    private readonly IBrowserActions _actions;
    // Вкладка не знает про файл настроек — текущий множитель спрашивает
    // через делегат у хоста, чтобы не тянуть зависимость на SettingsService.
    private readonly Func<double> _zoomProvider;
    // Движок создаётся лениво, а ApplyZoom могут позвать раньше:
    // значение ждёт создания CoreWebView2 и применяется в EnsureWebViewAsync.
    private double? _pendingZoom;
    private WebView2? _web;

    public event Action<Tab, string>? Navigated;
    public event Action<Tab>? StateChanged;
    public event Action<Tab, string>? NewWindowRequested;

    /// <summary>Страница (видео) вошла/вышла из HTML5-полноэкранного режима.</summary>
    public event Action<Tab, bool>? FullscreenChanged;

    public BrowserTabView(Tab tab, IBrowserActions actions, Func<double> zoomProvider)
    {
        InitializeComponent();
        _tab = tab;
        _actions = actions;
        // Хост всегда передаёт лямбду с актуальным значением — null здесь
        // означал бы вкладку в масштабе-наугад, поэтому падаем сразу.
        _zoomProvider = zoomProvider ?? throw new ArgumentNullException(nameof(zoomProvider));
    }

    /// <summary>true — движок WebView2 уже создан для этой вкладки.</summary>
    public bool HasEngine => _web is not null;

    /// <summary>true — страница сейчас в HTML5-полноэкранном режиме.</summary>
    public bool IsPageFullscreen { get; private set; }

    public bool CanGoBack => _web?.CoreWebView2?.CanGoBack == true;
    public bool CanGoForward => _web?.CoreWebView2?.CanGoForward == true;

    /// <summary>true — движок грузит страницу (для совмещённой кнопки тулбара).</summary>
    public bool IsLoading => _tab.IsLoading;

    public async Task NavigateAsync(string url)
    {
        try
        {
            if (!await EnsureWebViewAsync()) return;

            Overlay.Visibility = Visibility.Collapsed;
            _web!.Visibility = Visibility.Visible;
            _tab.IsAsleep = false;
            _web.CoreWebView2!.Navigate(url);
        }
        catch (Exception ex)
        {
            ShowOverlay($"Ошибка: {ex.Message}");
        }
    }

    public void GoBack()
    {
        if (_web?.CoreWebView2?.CanGoBack == true) _web.CoreWebView2.GoBack();
    }

    public void GoForward()
    {
        if (_web?.CoreWebView2?.CanGoForward == true) _web.CoreWebView2.GoForward();
    }

    public void Reload() => _web?.CoreWebView2?.Reload();

    public void Stop() => _web?.CoreWebView2?.Stop();

    /// <summary>Применить множитель масштаба к движку (или запомнить до его создания).</summary>
    public void ApplyZoom(double zoom)
    {
        // Проверяем именно готовность ядра, а не факт создания контрола:
        // между `new WebView2()` и концом `EnsureCoreWebView2Async` движка ещё
        // нет, и ZoomChanged() в это окно обязан уйти в отложенное значение,
        // а не в сеттер. Сам ZoomFactor берём у WPF-обёртки — в CoreWebView2
        // этого свойства нет в интероп-сборках пакета.
        if (_web?.CoreWebView2 is not null)
            _web.ZoomFactor = zoom;
        else
            _pendingZoom = zoom;
    }

    /// <summary>Перечитать множитель у хоста и применить — зовётся при смене настройки.</summary>
    public void ZoomChanged() => ApplyZoom(_zoomProvider());

    /// <summary>Выгрузить движок вкладки, сохраняя URL для будущего возврата.</summary>
    public void Sleep()
    {
        if (_web is null) return;

        if (_web.CoreWebView2 is not null)
        {
            var source = _web.Source?.ToString();
            if (!string.IsNullOrWhiteSpace(source)) _tab.Url = source;
        }

        try
        {
            _web.CoreWebView2?.Stop();
            _web.Dispose();
        }
        catch { /* уже закрыт */ }

        _web = null;
        // Уснувшая вкладка при пробуждении обязана взять свежий множитель,
        // а не устаревший — старое отложенное значение стираем.
        _pendingZoom = null;
        _tab.IsAsleep = true;
        // Движок уничтожен — полноэкранного элемента больше не существует.
        IsPageFullscreen = false;
        ShowOverlay("Вкладка приостановлена — память освобождена");
    }

    /// <summary>Полное освобождение ресурсов (закрытие вкладки/приложения).</summary>
    public void Shutdown()
    {
        try { _web?.Dispose(); } catch { }
        _web = null;
    }

    private async Task<bool> EnsureWebViewAsync()
    {
        if (_web?.CoreWebView2 is not null) return true;

        if (_web is null)
        {
            _web = new WebView2
            {
                Visibility = Visibility.Collapsed,
                // Тёмный фон вместо белой вспышки при загрузке страниц
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 0x1B, 0x1B, 0x1F),
            };
        }
        if (!Host.Children.Contains(_web))
        {
            Host.Children.Add(_web);
            Grid.SetZIndex(_web, 0);
        }

        try
        {
            var environment = await WebViewManager.GetEnvironmentAsync();
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            ShowOverlay($"Не удалось запустить движок: {ex.Message}");
            return false;
        }

        // Новая вкладка обязана открыться в текущем масштабе, а не в 100%:
        // отложенное значение (если ApplyZoom позвали до создания движка)
        // побеждает свежий замер провайдера.
        var zoom = _pendingZoom ?? _zoomProvider();
        // Отложенное значение потреблено — поле обязано отражать реальность
        // между созданием движка и сном, а не хранить вчерашний масштаб.
        _pendingZoom = null;
        ApplyZoom(zoom);
        WireEvents();
        return true;
    }

    private void WireEvents()
    {
        var core = _web!.CoreWebView2!;

        core.NavigationStarting += (_, e) =>
        {
            _tab.Url = e.Uri;
            // Флаг загрузки живёт на модели: по нему хост переключает кнопку
            // тулбара на «Остановить». SourceChanged про него не знает.
            _tab.IsLoading = true;
            StateChanged?.Invoke(_tab);
        };

        core.SourceChanged += (_, _) =>
        {
            _tab.Url = _web?.Source?.ToString() ?? _tab.Url;
            StateChanged?.Invoke(_tab);
        };

        core.DocumentTitleChanged += (_, _) =>
        {
            var title = core.DocumentTitle;
            _tab.Title = string.IsNullOrWhiteSpace(title) ? ShortHost(_tab.Url) : title;
        };

        core.NavigationCompleted += (_, e) =>
        {
            _tab.IsLoading = false;

            if (e.IsSuccess)
            {
                Overlay.Visibility = Visibility.Collapsed;
                _tab.IsAsleep = false;
                Navigated?.Invoke(_tab, _tab.Url);
            }
            else if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                // Пользователь нажал «Остановить»: это не ошибка. Движок сообщает
                // об отмене как о неуспехе, поэтому без этой ветки обычный стоп
                // выглядел бы как «страница не загрузилась».
                Overlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowOverlay("Страница не загрузилась — нажмите Enter для повтора");
            }
            StateChanged?.Invoke(_tab);
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true; // новые окна открываем своими вкладками
            NewWindowRequested?.Invoke(_tab, e.Uri);
        };

        // Страница ушла в HTML5-fullscreen (видео). WebView2 сам размер не меняет —
        // растягивается лишь область WebView2. Сообщаем хосту, чтобы он убрал chrome.
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            var fullscreen = core.ContainsFullScreenElement;
            IsPageFullscreen = fullscreen;
            if (_tab.IsActive) FullscreenChanged?.Invoke(_tab, fullscreen);
        };
    }

    private void ShowOverlay(string message)
    {
        OverlayText.Text = message;
        Overlay.Visibility = Visibility.Visible;
    }

    private static string ShortHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.Host;
        return "Вкладка";
    }
}
