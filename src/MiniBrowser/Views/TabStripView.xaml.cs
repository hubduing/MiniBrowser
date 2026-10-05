using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniBrowser.Models;

namespace MiniBrowser.Views;

/// <summary>Полоса вкладок: выбор, закрытие, создание новой вкладки.</summary>
public partial class TabStripView : UserControl
{
    public event Action<Tab>? TabActivated;
    public event Action<Tab>? TabCloseRequested;
    public event Action? NewTabRequested;

    /// <summary>Новое состояние списка вкладок (присваивать каждый раз новую коллекцию).</summary>
    public IEnumerable<Tab>? Items
    {
        set => TabsItems.ItemsSource = value;
    }

    public TabStripView() => InitializeComponent();

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsOverButton(e.OriginalSource as DependencyObject)) return;
        if (sender is FrameworkElement { DataContext: Tab tab })
            TabActivated?.Invoke(tab);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Tab tab })
            TabCloseRequested?.Invoke(tab);
    }

    /// <summary>Клик по крестику не должен переключать вкладку.</summary>
    private static bool IsOverButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is Button) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }
}
