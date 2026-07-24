using System.Collections.ObjectModel;
using System.Windows;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Application.Services;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.UI.Configuration;
using TiaProjectExporter.UI.Logging;

namespace TiaProjectExporter.UI.ViewModels;

/// <summary>
/// Main window view model.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private const int MaxRecentOutputDirectories = 10;
    private readonly ITiaInstallationDiscoveryService _installationDiscoveryService;
    private readonly ExportCoordinator _exportCoordinator;
    private readonly IExporterSettingsStore _settingsStore;
    private readonly UiLogCollector _logCollector;
    private string _projectPath = string.Empty;
    private string _outputDirectory;
    private bool _exportJson = true;
    private bool _exportXml = true;
    private bool _exportMarkdown;
    private bool _enableCompression;
    private bool _skipDiagnostics;
    private string _statusText = "Ready";
    private string _progressText = "No export started";
    private string _currentObject = "Waiting for action";
    private string _estimatedRemainingText = "Estimated remaining time will appear during export.";
    private double _progressPercent;
    private int _succeededCount;
    private int _failedCount;
    private int _issueCount;
    private bool _isExporting;
    private CancellationTokenSource? _exportCancellationTokenSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    public MainWindowViewModel(
        ITiaInstallationDiscoveryService installationDiscoveryService,
        ExportCoordinator exportCoordinator,
        ExporterSettings settings,
        IExporterSettingsStore settingsStore,
        UiLogCollector logCollector)
    {
        _installationDiscoveryService = installationDiscoveryService;
        _exportCoordinator = exportCoordinator;
        _settingsStore = settingsStore;
        _logCollector = logCollector;

        _outputDirectory = settings.DefaultOutputDirectory;
        _exportMarkdown = settings.GenerateMarkdownSummaries;
        _enableCompression = settings.EnableCompression;
        _skipDiagnostics = settings.SkipDiagnostics;

        DiscoverVersionsCommand = new AsyncRelayCommand(DiscoverVersionsAsync, onExceptionAsync: HandleCommandExceptionAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport, HandleCommandExceptionAsync);
        CancelExportCommand = new AsyncRelayCommand(CancelExportAsync, CanCancelExport, HandleCommandExceptionAsync);

        logCollector.EntryAdded += OnLogEntryAdded;

        LoadPersistedSettings();
    }

    /// <summary>
    /// Gets the discovered TIA installations.
    /// </summary>
    public ObservableCollection<DiscoveredTiaPortalInstallation> Installations { get; } = [];

    /// <summary>
    /// Gets the UI log entries.
    /// </summary>
    public ObservableCollection<string> LogEntries { get; } = [];

    /// <summary>
    /// Gets recent output directories used by the exporter.
    /// </summary>
    public ObservableCollection<string> RecentOutputDirectories { get; } = [];

    /// <summary>
    /// Gets the command that detects installed TIA versions.
    /// </summary>
    public AsyncRelayCommand DiscoverVersionsCommand { get; }

    /// <summary>
    /// Gets the command that starts the export.
    /// </summary>
    public AsyncRelayCommand ExportCommand { get; }

    /// <summary>
    /// Gets the command that requests cancellation of the current export run.
    /// </summary>
    public AsyncRelayCommand CancelExportCommand { get; }

    /// <summary>
    /// Gets or sets the output directory.
    /// </summary>
    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the source TIA project path.
    /// </summary>
    public string ProjectPath
    {
        get => _projectPath;
        set => SetProperty(ref _projectPath, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether JSON should be exported.
    /// </summary>
    public bool ExportJson
    {
        get => _exportJson;
        set => SetProperty(ref _exportJson, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether XML should be exported.
    /// </summary>
    public bool ExportXml
    {
        get => _exportXml;
        set => SetProperty(ref _exportXml, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether markdown should be exported.
    /// </summary>
    public bool ExportMarkdown
    {
        get => _exportMarkdown;
        set => SetProperty(ref _exportMarkdown, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether compression is enabled.
    /// </summary>
    public bool EnableCompression
    {
        get => _enableCompression;
        set => SetProperty(ref _enableCompression, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether diagnostics are skipped.
    /// </summary>
    public bool SkipDiagnostics
    {
        get => _skipDiagnostics;
        set => SetProperty(ref _skipDiagnostics, value);
    }

    /// <summary>
    /// Gets or sets the current status text.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// Gets or sets the progress headline text.
    /// </summary>
    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    /// <summary>
    /// Gets or sets the current object text.
    /// </summary>
    public string CurrentObject
    {
        get => _currentObject;
        set => SetProperty(ref _currentObject, value);
    }

    /// <summary>
    /// Gets or sets the estimated remaining time text.
    /// </summary>
    public string EstimatedRemainingText
    {
        get => _estimatedRemainingText;
        set => SetProperty(ref _estimatedRemainingText, value);
    }

    /// <summary>
    /// Gets or sets the progress percent.
    /// </summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    /// <summary>
    /// Gets or sets the succeeded result count.
    /// </summary>
    public int SucceededCount
    {
        get => _succeededCount;
        set => SetProperty(ref _succeededCount, value);
    }

    /// <summary>
    /// Gets or sets the failed result count.
    /// </summary>
    public int FailedCount
    {
        get => _failedCount;
        set => SetProperty(ref _failedCount, value);
    }

    /// <summary>
    /// Gets or sets the issue count.
    /// </summary>
    public int IssueCount
    {
        get => _issueCount;
        set => SetProperty(ref _issueCount, value);
    }

    /// <summary>
    /// Persists the current user settings to local storage.
    /// </summary>
    public Task PersistSettingsAsync(CancellationToken cancellationToken) => SavePersistedSettingsAsync(cancellationToken);

    /// <summary>
    /// Gets or sets a value indicating whether an export is currently running.
    /// </summary>
    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
                CancelExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task DiscoverVersionsAsync()
    {
        StatusText = "Detecting installed TIA versions";
        Installations.Clear();

        var installations = await _installationDiscoveryService.DiscoverAsync(CancellationToken.None);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            foreach (var installation in installations.OrderBy(item => item.Version))
            {
                Installations.Add(installation);
            }
        });

        StatusText = installations.Count == 0
            ? "No supported TIA versions detected"
            : $"Detected {installations.Count} supported TIA version(s)";
    }

    private async Task ExportAsync()
    {
        StatusText = "Running export";
        ProgressText = "Preparing repository export";
        CurrentObject = "Initializing";
        ProgressPercent = 0;
        SucceededCount = 0;
        FailedCount = 0;
        IssueCount = 0;

        using var cancellationTokenSource = new CancellationTokenSource();
        _exportCancellationTokenSource = cancellationTokenSource;
        IsExporting = true;
        AddRecentOutputDirectory(OutputDirectory);

        try
        {
            var options = new ExportOptions(
                string.IsNullOrWhiteSpace(ProjectPath) ? null : ProjectPath,
                OutputDirectory,
                BuildFormats(),
                EnableCompression,
                SkipDiagnostics,
                ExportMarkdown);

            var report = await _exportCoordinator.ExecuteAsync(options, HandleProgressAsync, cancellationTokenSource.Token);

            SucceededCount = report.SucceededCount;
            FailedCount = report.FailedCount;
            IssueCount = report.Issues.Count;
            StatusText = report.FailedCount == 0 ? "Export completed" : "Export completed with issues";
            ProgressText = $"{report.SucceededCount} succeeded, {report.FailedCount} failed";
            CurrentObject = "Export finished";
            EstimatedRemainingText = "No remaining work";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Export canceled";
            ProgressText = "Export canceled by user";
            CurrentObject = "Cancellation requested";
            EstimatedRemainingText = "No remaining work";
            _logCollector.Add("Export canceled by user request.");
        }
        finally
        {
            await SavePersistedSettingsAsync(CancellationToken.None);
            _exportCancellationTokenSource = null;
            IsExporting = false;
        }
    }

    private Task CancelExportAsync()
    {
        if (_exportCancellationTokenSource is { IsCancellationRequested: false })
        {
            _exportCancellationTokenSource.Cancel();
            StatusText = "Cancelling export";
            CurrentObject = "Waiting for active stage to stop";
            EstimatedRemainingText = "Cancellation in progress";
        }

        return Task.CompletedTask;
    }

    private Task HandleProgressAsync(ExportProgressUpdate update)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ProgressText = update.CurrentStage;
            CurrentObject = update.CurrentObject;
            ProgressPercent = update.TotalItems is > 0
                ? (double)update.ProcessedItems / update.TotalItems.Value * 100
                : 20;
            EstimatedRemainingText = update.EstimatedRemaining is null
                ? "Estimating remaining time"
                : $"Estimated remaining: {update.EstimatedRemaining.Value:g}";
        }).Task;
    }

    private bool CanExport() => !IsExporting && !string.IsNullOrWhiteSpace(OutputDirectory);

    private bool CanCancelExport() => IsExporting && _exportCancellationTokenSource is { IsCancellationRequested: false };

    private void LoadPersistedSettings()
    {
        var persisted = _settingsStore.Load();

        ProjectPath = string.IsNullOrWhiteSpace(persisted.LastProjectPath)
            ? ProjectPath
            : persisted.LastProjectPath;

        OutputDirectory = string.IsNullOrWhiteSpace(persisted.LastOutputDirectory)
            ? OutputDirectory
            : persisted.LastOutputDirectory;

        ExportJson = persisted.ExportJson;
        ExportXml = persisted.ExportXml;
        ExportMarkdown = persisted.ExportMarkdown;
        EnableCompression = persisted.EnableCompression;
        SkipDiagnostics = persisted.SkipDiagnostics;

        foreach (var directory in persisted.RecentOutputDirectories)
        {
            AddRecentOutputDirectory(directory);
        }
    }

    private async Task SavePersistedSettingsAsync(CancellationToken cancellationToken)
    {
        var persisted = new PersistedExporterSettings
        {
            LastProjectPath = ProjectPath,
            LastOutputDirectory = OutputDirectory,
            ExportJson = ExportJson,
            ExportXml = ExportXml,
            ExportMarkdown = ExportMarkdown,
            EnableCompression = EnableCompression,
            SkipDiagnostics = SkipDiagnostics,
            RecentOutputDirectories = RecentOutputDirectories.ToList()
        };

        await _settingsStore.SaveAsync(persisted, cancellationToken).ConfigureAwait(false);
    }

    private void AddRecentOutputDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var existingIndex = RecentOutputDirectories
            .Select((value, index) => new { Value = value, Index = index })
            .FirstOrDefault(entry => string.Equals(entry.Value, directory, StringComparison.OrdinalIgnoreCase))
            ?.Index;

        if (existingIndex is int index)
        {
            RecentOutputDirectories.RemoveAt(index);
        }

        RecentOutputDirectories.Insert(0, directory);

        while (RecentOutputDirectories.Count > MaxRecentOutputDirectories)
        {
            RecentOutputDirectories.RemoveAt(RecentOutputDirectories.Count - 1);
        }
    }

    private Task HandleCommandExceptionAsync(Exception exception)
    {
        StatusText = "Operation failed";
        CurrentObject = exception.Message;
        EstimatedRemainingText = "Operation aborted";
        _logCollector.Add($"UI command failure: {exception}");
        return Task.CompletedTask;
    }

    private IReadOnlyCollection<ExportFormat> BuildFormats()
    {
        var formats = new List<ExportFormat>();

        if (ExportJson)
        {
            formats.Add(ExportFormat.Json);
        }

        if (ExportXml)
        {
            formats.Add(ExportFormat.Xml);
        }

        if (ExportMarkdown)
        {
            formats.Add(ExportFormat.Markdown);
        }

        if (formats.Count == 0)
        {
            formats.Add(ExportFormat.Json);
        }

        return formats;
    }

    private void OnLogEntryAdded(object? sender, string entry)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogEntries.Add(entry);

            while (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }
}
