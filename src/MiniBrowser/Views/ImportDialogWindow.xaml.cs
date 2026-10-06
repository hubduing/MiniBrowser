using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniBrowser.Services;
using MiniBrowser.Services.Import;

namespace MiniBrowser.Views;

/// <summary>
/// Диалог импорта закладок и паролей из установленного браузера
/// (по образцу предложения импорта при первом запуске Firefox).
/// </summary>
public partial class ImportDialogWindow : Window
{
    private readonly BrowserImportService _service;

    public ImportDialogWindow(StorageService storage, PasswordStore passwords,
        IReadOnlyList<BrowserProfile> profiles)
    {
        InitializeComponent();
        _service = new BrowserImportService(storage, passwords);

        BrowserBox.ItemsSource = profiles;
        if (profiles.Count == 0)
        {
            NoBrowsersText.Visibility = Visibility.Visible;
            ImportButton.IsEnabled = false;
        }
        else
        {
            BrowserBox.SelectedIndex = 0;
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserBox.SelectedItem is not BrowserProfile profile) return;
        var withBookmarks = BookmarksBox.IsChecked == true;
        var withLogins = LoginsBox.IsChecked == true;
        if (!withBookmarks && !withLogins) return;

        ImportButton.IsEnabled = false;
        ProgressText.Text = "Импортируем…";
        try
        {
            // Чтение чужих файлов и расшифровка — в фоне, окно не замирает.
            var result = await Task.Run(() => _service.Import(profile, withBookmarks, withLogins));

            var parts = new List<string>();
            if (withBookmarks)
                parts.Add($"Закладок: {result.BookmarksAdded} · пропущено: {result.BookmarksSkipped}");
            if (withLogins)
                parts.Add($"Паролей: {result.PasswordsAdded} · пропущено: {result.PasswordsSkipped}");
            SummaryText.Text = string.Join("   ", parts);
            foreach (var note in result.Notes)
                NotesPanel.Children.Add(new TextBlock
                {
                    Text = note,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = (System.Windows.Media.Brush)FindResource("T.Muted"),
                });

            ProgressText.Text = "";
            DoneButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ProgressText.Text = "";
            SummaryText.Text = $"Импорт не удался: {ex.Message}";
            ImportButton.IsEnabled = true;
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
