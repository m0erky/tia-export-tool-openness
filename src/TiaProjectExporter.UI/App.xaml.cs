using System.Windows;
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

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync().ConfigureAwait(true);
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
