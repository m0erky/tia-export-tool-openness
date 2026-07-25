using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TiaProjectExporter.Application;
using TiaProjectExporter.Export;
using TiaProjectExporter.Infrastructure;
using TiaProjectExporter.Tia;
using TiaProjectExporter.UI.Configuration;
using TiaProjectExporter.UI.Logging;
using TiaProjectExporter.UI.Services;
using TiaProjectExporter.UI.ViewModels;

namespace TiaProjectExporter.UI;

/// <summary>
/// WPF application bootstrapper.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(static (_, configuration) =>
                {
                    configuration.Sources.Clear();
                    configuration.SetBasePath(AppContext.BaseDirectory);
                    configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices(static (context, services) =>
                {
                    var exporterSettings = new ExporterSettings();
                    context.Configuration.GetSection("Exporter").Bind(exporterSettings);

                    services.AddSingleton(exporterSettings);
                    services.AddSingleton<IExporterSettingsStore, JsonExporterSettingsStore>();
                    services.AddSingleton<IFolderSelectionService, WindowsFolderSelectionService>();
                    services.AddApplicationServices();
                    services.AddInfrastructure();
                    services.AddTiaServices();
                    services.AddExportServices();

                    services.AddSingleton<UiLogCollector>();
                    services.AddSingleton<ILoggerProvider, UiLoggerProvider>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .ConfigureLogging(static (context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddConfiguration(context.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                })
                .Build();

            await _host.StartAsync().ConfigureAwait(true);

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Application startup failed.\n\n{exception}",
                "TIA Project Exporter",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                var viewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
                await viewModel.PersistSettingsAsync(CancellationToken.None).ConfigureAwait(true);
                await _host.StopAsync().ConfigureAwait(true);
                _host.Dispose();
            }
        }
        catch
        {
            // Suppress shutdown-time exceptions to avoid crash dialogs.
        }
        finally
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnTaskSchedulerUnobservedTaskException;
        }

        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Unexpected error:\n\n{e.Exception.Message}",
            "TIA Project Exporter",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            MessageBox.Show(
                $"Unhandled application error:\n\n{exception.Message}",
                "TIA Project Exporter",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
    }
}
