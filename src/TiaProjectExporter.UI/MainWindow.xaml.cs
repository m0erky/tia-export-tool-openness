using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TiaProjectExporter.UI.ViewModels;

namespace TiaProjectExporter.UI;

/// <summary>
/// Main desktop shell.
/// </summary>
public partial class MainWindow : Window
{
    private ScrollViewer? _logScrollViewer;
    private bool _autoScrollLog = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _logScrollViewer = FindDescendant<ScrollViewer>(LogOutputListBox);

        if (_logScrollViewer is not null)
        {
            _logScrollViewer.ScrollChanged += OnLogScrollChanged;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.LogEntries.CollectionChanged += OnLogEntriesCollectionChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_logScrollViewer is not null)
        {
            _logScrollViewer.ScrollChanged -= OnLogScrollChanged;
            _logScrollViewer = null;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.LogEntries.CollectionChanged -= OnLogEntriesCollectionChanged;
        }
    }

    private void OnLogEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            return;
        }

        if (!_autoScrollLog)
        {
            return;
        }

        ScrollLogToLatest();
    }

    private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_logScrollViewer is null)
        {
            return;
        }

        const double epsilon = 8;
        var distanceFromBottom = _logScrollViewer.ScrollableHeight - _logScrollViewer.VerticalOffset;
        _autoScrollLog = distanceFromBottom <= epsilon;
    }

    private void OnJumpToLatestLogClicked(object sender, RoutedEventArgs e)
    {
        _autoScrollLog = true;
        ScrollLogToLatest();
    }

    private void ScrollLogToLatest()
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.LogEntries.Count == 0)
        {
            return;
        }

        var latestEntry = viewModel.LogEntries[^1];
        LogOutputListBox.ScrollIntoView(latestEntry);
        _logScrollViewer?.ScrollToEnd();
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T typed)
        {
            return typed;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            var result = FindDescendant<T>(child);

            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
