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
    private WebView2? _web;

    public event Action<Tab, string>? Navigated;
    public event Action<Tab>? StateChanged;
    public event Action<Tab, string>? NewWindowRequested;

    public BrowserTabView(Tab tab, IBrowserActions actions)
    {
        InitializeComponent();
        _tab = tab;
        _actions = actions;
    }

    /// <summary>true — движок WebView2 уже создан для этой вкладки.</summary>
    public bool HasEngine => _web is not null;

    public bool CanGoBack => _web?.CoreWebView2?.CanGoBack == true;
    public bool CanGoForward => _web?.CoreWebView2?.CanGoForward == true;

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
        _tab.IsAsleep = true;
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
            _web = new WebView2 { Visibility = Visibility.Collapsed };
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

        WireEvents();
        return true;
    }

    private void WireEvents()
    {
        var core = _web!.CoreWebView2!;

        core.NavigationStarting += (_, e) => _tab.Url = e.Uri;

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
            if (e.IsSuccess)
            {
                Overlay.Visibility = Visibility.Collapsed;
                _tab.IsAsleep = false;
                Navigated?.Invoke(_tab, _tab.Url);
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
