using System.IO;
using Microsoft.Web.WebView2.Core;

namespace MiniBrowser.Services;

/// <summary>
/// Один общий CoreWebView2Environment на все вкладки:
/// браузерный/GPU-процессы и кэши переиспользуются, а не создаются на вкладку.
/// </summary>
public static class WebViewManager
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static CoreWebView2Environment? _environment;

    public static string UserDataFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiniBrowser", "WebView2");

    public static async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environment is not null) return _environment;

        await Lock.WaitAsync();
        try
        {
            _environment ??= await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder);
            return _environment;
        }
        finally
        {
            Lock.Release();
        }
    }
}
