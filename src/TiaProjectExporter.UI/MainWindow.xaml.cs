using System.Windows;
using TiaProjectExporter.UI.ViewModels;

namespace TiaProjectExporter.UI;

/// <summary>
/// Main desktop shell.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

