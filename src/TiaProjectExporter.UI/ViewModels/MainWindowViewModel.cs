using System.Collections.ObjectModel;
using System.Text;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Application.Services;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Tia.Inventory;
using TiaProjectExporter.UI.Configuration;
using TiaProjectExporter.UI.Logging;
using TiaProjectExporter.UI.Services;

namespace TiaProjectExporter.UI.ViewModels;

/// <summary>
/// Main window view model.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private const int MaxRecentOutputDirectories = 10;
    private readonly ITiaInstallationDiscoveryService _installationDiscoveryService;
    private readonly IOpennessHealthCheckService _opennessHealthCheckService;
    private readonly ExportCoordinator _exportCoordinator;
    private readonly IExporterSettingsStore _settingsStore;
    private readonly IFolderSelectionService _folderSelectionService;
    private readonly UiLogCollector _logCollector;
    private string _projectPath = string.Empty;
    private string _tiaInstallationPathOverride = string.Empty;
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
    private string _projectPathValidationText = "Set a TIA project path (.ap18/.ap19/.ap20) before export.";
    private string _tiaInstallationValidationText = "Manual TIA installation override is optional.";
    private string _healthCheckStatusText = "Health check not executed yet.";
    private string _healthCheckIndicatorBrush = "#6B7280";
    private string _hostActivityStatusText = "No host heartbeat received yet.";
    private string _hostActivityIndicatorBrush = "#6B7280";
    private DateTimeOffset? _lastHostHeartbeatUtc;
    private bool _isExporting;
    private CancellationTokenSource? _exportCancellationTokenSource;
    private readonly DispatcherTimer _hostHeartbeatTimer;

    private static readonly string AppVersion = ResolveAppVersion();

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
    /// </summary>
    public MainWindowViewModel(
        ITiaInstallationDiscoveryService installationDiscoveryService,
        IOpennessHealthCheckService opennessHealthCheckService,
        ExportCoordinator exportCoordinator,
        ExporterSettings settings,
        IExporterSettingsStore settingsStore,
        IFolderSelectionService folderSelectionService,
        UiLogCollector logCollector)
    {
        _installationDiscoveryService = installationDiscoveryService;
        _opennessHealthCheckService = opennessHealthCheckService;
        _exportCoordinator = exportCoordinator;
        _settingsStore = settingsStore;
        _folderSelectionService = folderSelectionService;
        _logCollector = logCollector;

        _outputDirectory = settings.DefaultOutputDirectory;
        _exportMarkdown = settings.GenerateMarkdownSummaries;
        _enableCompression = settings.EnableCompression;
        _skipDiagnostics = settings.SkipDiagnostics;

        DiscoverVersionsCommand = new AsyncRelayCommand(DiscoverVersionsAsync, onExceptionAsync: HandleCommandExceptionAsync);
        RunHealthCheckCommand = new AsyncRelayCommand(RunHealthCheckAsync, onExceptionAsync: HandleCommandExceptionAsync);
        BrowseProjectPathCommand = new AsyncRelayCommand(BrowseProjectPathAsync, onExceptionAsync: HandleCommandExceptionAsync);
        ValidateProjectPathCommand = new AsyncRelayCommand(ValidateProjectPathAsync, onExceptionAsync: HandleCommandExceptionAsync);
        BrowseTiaInstallationPathOverrideCommand = new AsyncRelayCommand(BrowseTiaInstallationPathOverrideAsync, onExceptionAsync: HandleCommandExceptionAsync);
        ValidateTiaInstallationPathOverrideCommand = new AsyncRelayCommand(ValidateTiaInstallationPathOverrideAsync, onExceptionAsync: HandleCommandExceptionAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport, HandleCommandExceptionAsync);
        CancelExportCommand = new AsyncRelayCommand(CancelExportAsync, CanCancelExport, HandleCommandExceptionAsync);

        logCollector.EntryAdded += OnLogEntryAdded;

        _hostHeartbeatTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnHostHeartbeatTimerTick, Dispatcher.CurrentDispatcher);
        _hostHeartbeatTimer.Start();

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
    /// Gets the command that executes an Openness runtime health check.
    /// </summary>
    public AsyncRelayCommand RunHealthCheckCommand { get; }

    /// <summary>
    /// Gets the command that opens a file picker for the source TIA project path.
    /// </summary>
    public AsyncRelayCommand BrowseProjectPathCommand { get; }

    /// <summary>
    /// Gets the command that validates the selected TIA project path.
    /// </summary>
    public AsyncRelayCommand ValidateProjectPathCommand { get; }

    /// <summary>
    /// Gets the command that opens a folder picker for the manual TIA installation path override.
    /// </summary>
    public AsyncRelayCommand BrowseTiaInstallationPathOverrideCommand { get; }

    /// <summary>
    /// Gets the command that validates the manual TIA installation path override.
    /// </summary>
    public AsyncRelayCommand ValidateTiaInstallationPathOverrideCommand { get; }

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
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
                ProjectPathValidationText = string.IsNullOrWhiteSpace(value)
                    ? "Set a TIA project path (.ap18/.ap19/.ap20) before export."
                    : "Path changed. Click 'Validate Project' to verify availability.";
            }
        }
    }

    /// <summary>
    /// Gets the window title with the current app version.
    /// </summary>
    public string WindowTitle => $"TIA Project Exporter v{AppVersion}";

    /// <summary>
    /// Gets the display-friendly application version.
    /// </summary>
    public string VersionText => $"Version {AppVersion}";

    /// <summary>
    /// Gets or sets the high-level runtime health-check status text.
    /// </summary>
    public string HealthCheckStatusText
    {
        get => _healthCheckStatusText;
        set => SetProperty(ref _healthCheckStatusText, value);
    }

    /// <summary>
    /// Gets or sets the indicator brush for runtime health status.
    /// </summary>
    public string HealthCheckIndicatorBrush
    {
        get => _healthCheckIndicatorBrush;
        set => SetProperty(ref _healthCheckIndicatorBrush, value);
    }

    /// <summary>
    /// Gets or sets the host-runtime liveness status text derived from heartbeats.
    /// </summary>
    public string HostActivityStatusText
    {
        get => _hostActivityStatusText;
        set => SetProperty(ref _hostActivityStatusText, value);
    }

    /// <summary>
    /// Gets or sets the host-runtime liveness indicator brush.
    /// </summary>
    public string HostActivityIndicatorBrush
    {
        get => _hostActivityIndicatorBrush;
        set => SetProperty(ref _hostActivityIndicatorBrush, value);
    }

    /// <summary>
    /// Gets or sets the validation feedback for the selected project path.
    /// </summary>
    public string ProjectPathValidationText
    {
        get => _projectPathValidationText;
        set => SetProperty(ref _projectPathValidationText, value);
    }

    /// <summary>
    /// Gets or sets an optional manual override path for TIA installation root.
    /// </summary>
    public string TiaInstallationPathOverride
    {
        get => _tiaInstallationPathOverride;
        set
        {
            if (SetProperty(ref _tiaInstallationPathOverride, value))
            {
                TiaInstallationValidationText = string.IsNullOrWhiteSpace(value)
                    ? "Manual TIA installation override is optional."
                    : "Path changed. Click 'Validate Path' to verify TIA V20 + Openness runtime.";
            }
        }
    }

    /// <summary>
    /// Gets or sets the validation feedback for the manual TIA installation override path.
    /// </summary>
    public string TiaInstallationValidationText
    {
        get => _tiaInstallationValidationText;
        set => SetProperty(ref _tiaInstallationValidationText, value);
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

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
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

    private async Task RunHealthCheckAsync()
    {
        StatusText = "Running Openness health check";
        HealthCheckStatusText = "Running health check...";
        HealthCheckIndicatorBrush = "#F59E0B";

        var result = await _opennessHealthCheckService
            .CheckAsync(string.IsNullOrWhiteSpace(TiaInstallationPathOverride) ? null : TiaInstallationPathOverride.Trim(), CancellationToken.None);

        var summary = string.IsNullOrWhiteSpace(result.Summary)
            ? "No summary available"
            : result.Summary;

        HealthCheckStatusText = result.Details.Count > 0
            ? $"{summary} | {result.Details[0]}"
            : summary;

        HealthCheckIndicatorBrush = result.State switch
        {
            OpennessHealthCheckState.Healthy => "#16A34A",
            OpennessHealthCheckState.Warning => "#F59E0B",
            _ => "#DC2626"
        };

        StatusText = "Health check completed";

        foreach (var detail in result.Details)
        {
            _logCollector.Add($"HealthCheck: {detail}");
        }
    }

    private Task BrowseProjectPathAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select TIA Project File",
            Filter = "TIA Project (*.ap18;*.ap19;*.ap20)|*.ap18;*.ap19;*.ap20|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (File.Exists(ProjectPath))
        {
            dialog.FileName = ProjectPath;
            dialog.InitialDirectory = Path.GetDirectoryName(ProjectPath) ?? string.Empty;
        }
        else if (Directory.Exists(ProjectPath))
        {
            dialog.InitialDirectory = ProjectPath;
        }

        var result = dialog.ShowDialog();

        if (result == true)
        {
            ProjectPath = dialog.FileName;
        }

        return Task.CompletedTask;
    }

    private Task ValidateProjectPathAsync()
    {
        var validation = ValidateProjectPath(ProjectPath);
        ProjectPathValidationText = validation.Message;
        return Task.CompletedTask;
    }

    private Task BrowseTiaInstallationPathOverrideAsync()
    {
        var selectedPath = _folderSelectionService.PickFolder(TiaInstallationPathOverride);

        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            TiaInstallationPathOverride = selectedPath;
        }

        return Task.CompletedTask;
    }

    private Task ValidateTiaInstallationPathOverrideAsync()
    {
        var candidatePath = TiaInstallationPathOverride?.Trim();

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            TiaInstallationValidationText = "No override path set. Detection will use discovered installations.";
            return Task.CompletedTask;
        }

        if (!Directory.Exists(candidatePath))
        {
            TiaInstallationValidationText = "Invalid path: directory does not exist.";
            return Task.CompletedTask;
        }

        var isV20Path = OpennessRuntimeLocator.IsLikelyV20InstallationPath(candidatePath);
        var engineeringAssemblyPath = OpennessRuntimeLocator.ResolveEngineeringAssemblyPath(candidatePath);
        var opennessDetected = !string.IsNullOrWhiteSpace(engineeringAssemblyPath);

        if (isV20Path && opennessDetected)
        {
            TiaInstallationValidationText = $"Valid: TIA V20 + Openness found ({engineeringAssemblyPath}).";
            return Task.CompletedTask;
        }

        if (!isV20Path && opennessDetected)
        {
            TiaInstallationValidationText = "Openness runtime was found, but this does not look like a TIA V20 installation path.";
            return Task.CompletedTask;
        }

        if (isV20Path)
        {
            TiaInstallationValidationText = "TIA V20 path detected, but Siemens.Engineering.dll was not found (Openness missing).";
            return Task.CompletedTask;
        }

        TiaInstallationValidationText = "Path does not look like TIA V20 and Openness runtime was not found.";
        return Task.CompletedTask;
    }

    private async Task ExportAsync()
    {
        var projectPathValidation = ValidateProjectPath(ProjectPath);

        if (!projectPathValidation.IsValid)
        {
            StatusText = "Export not started";
            CurrentObject = "Invalid project path";
            ProjectPathValidationText = projectPathValidation.Message;
            _logCollector.Add($"Export aborted: {projectPathValidation.Message}");
            return;
        }

        var outputValidation = ValidateOutputDirectory(OutputDirectory);

        if (!outputValidation.IsValid)
        {
            StatusText = "Export not started";
            CurrentObject = "Invalid output directory";
            _logCollector.Add($"Export aborted: {outputValidation.Message}");
            return;
        }

        var healthCheckResult = await _opennessHealthCheckService
            .CheckAsync(string.IsNullOrWhiteSpace(TiaInstallationPathOverride) ? null : TiaInstallationPathOverride.Trim(), CancellationToken.None);

        HealthCheckIndicatorBrush = healthCheckResult.State switch
        {
            OpennessHealthCheckState.Healthy => "#16A34A",
            OpennessHealthCheckState.Warning => "#F59E0B",
            _ => "#DC2626"
        };

        HealthCheckStatusText = healthCheckResult.Details.Count > 0
            ? $"{healthCheckResult.Summary} | {healthCheckResult.Details[0]}"
            : healthCheckResult.Summary;

        foreach (var detail in healthCheckResult.Details)
        {
            _logCollector.Add($"ExportPreflight: {detail}");
        }

        if (healthCheckResult.State == OpennessHealthCheckState.Unhealthy)
        {
            StatusText = "Export not started";
            CurrentObject = "Openness health check failed";
            _logCollector.Add($"Export aborted: {healthCheckResult.Summary}");
            return;
        }

        StatusText = "Running export";
        ProgressText = "Preparing repository export";
        CurrentObject = "Initializing";
        _lastHostHeartbeatUtc = null;
        HostActivityIndicatorBrush = "#F59E0B";
        HostActivityStatusText = "Waiting for first host heartbeat...";
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
                ExportMarkdown,
                string.IsNullOrWhiteSpace(TiaInstallationPathOverride) ? null : TiaInstallationPathOverride.Trim());

            var report = await _exportCoordinator.ExecuteAsync(options, HandleProgressAsync, cancellationTokenSource.Token);

            SucceededCount = report.SucceededCount;
            FailedCount = report.FailedCount;
            IssueCount = report.Issues.Count;
            StatusText = report.FailedCount == 0 && report.Issues.Count == 0
                ? "Export completed"
                : "Export completed with issues";
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
        catch (Exception exception)
        {
            StatusText = "Export failed";
            ProgressText = "Export aborted due to an error";
            CurrentObject = exception.Message;
            EstimatedRemainingText = "Operation aborted";
            _logCollector.Add($"Export failed: {exception}");

            var diagnosticsPath = await WriteFailureDiagnosticsAsync(exception, CancellationToken.None).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(diagnosticsPath))
            {
                _logCollector.Add($"Failure diagnostics written to: {diagnosticsPath}");
            }
        }
        finally
        {
            await SavePersistedSettingsAsync(CancellationToken.None);
            _exportCancellationTokenSource = null;
            IsExporting = false;

            if (_lastHostHeartbeatUtc is not null)
            {
                HostActivityIndicatorBrush = "#6B7280";
                HostActivityStatusText = "Host heartbeat monitoring idle.";
            }
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
        return System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
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

    private bool CanExport() =>
        !IsExporting
        && !string.IsNullOrWhiteSpace(OutputDirectory)
        && !string.IsNullOrWhiteSpace(ProjectPath);

    private bool CanCancelExport() => IsExporting && _exportCancellationTokenSource is { IsCancellationRequested: false };

    private void LoadPersistedSettings()
    {
        var persisted = _settingsStore.Load();

        ProjectPath = string.IsNullOrWhiteSpace(persisted.LastProjectPath)
            ? ProjectPath
            : persisted.LastProjectPath;

        if (!string.IsNullOrWhiteSpace(ProjectPath))
        {
            ProjectPathValidationText = "Loaded persisted project path. Click 'Validate Project' to verify availability.";
        }

        TiaInstallationPathOverride = string.IsNullOrWhiteSpace(persisted.TiaInstallationPathOverride)
            ? TiaInstallationPathOverride
            : persisted.TiaInstallationPathOverride;

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
            TiaInstallationPathOverride = TiaInstallationPathOverride,
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

    private async Task HandleCommandExceptionAsync(Exception exception)
    {
        StatusText = "Operation failed";
        CurrentObject = exception.Message;
        EstimatedRemainingText = "Operation aborted";
        _logCollector.Add($"UI command failure: {exception}");

        var diagnosticsPath = await WriteFailureDiagnosticsAsync(exception, CancellationToken.None).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(diagnosticsPath))
        {
            _logCollector.Add($"Failure diagnostics written to: {diagnosticsPath}");
        }
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

    private static (bool IsValid, string Message) ValidateProjectPath(string? projectPath)
    {
        var candidate = projectPath?.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return (false, "Project path is required.");
        }

        var existsAsFile = File.Exists(candidate);
        var existsAsDirectory = Directory.Exists(candidate);

        if (!existsAsFile && !existsAsDirectory)
        {
            return (false, "Project path does not exist.");
        }

        var extension = existsAsFile
            ? Path.GetExtension(candidate)
            : Path.GetExtension(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var isKnownTiaProjectExtension = string.Equals(extension, ".ap18", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".ap19", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".ap20", StringComparison.OrdinalIgnoreCase);

        if (!isKnownTiaProjectExtension)
        {
            return (false, "Project path must point to a .ap18, .ap19, or .ap20 project.");
        }

        return (true, $"Valid project path: {candidate}");
    }

    private static (bool IsValid, string Message) ValidateOutputDirectory(string? outputDirectory)
    {
        var candidate = outputDirectory?.Trim();

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return (false, "Output directory is required.");
        }

        try
        {
            Directory.CreateDirectory(candidate);
            var probePath = Path.Combine(candidate, ".tia-exporter-write-test.tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
            return (true, $"Output directory is writable: {candidate}");
        }
        catch (Exception exception)
        {
            return (false, $"Output directory is not writable: {exception.Message}");
        }
    }

    private static string ResolveAppVersion()
    {
        var informationalVersion = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var plusIndex = informationalVersion.IndexOf('+');
            return plusIndex > 0
                ? informationalVersion[..plusIndex]
                : informationalVersion;
        }

        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        if (assemblyVersion is null)
        {
            return "0.0.13";
        }

        return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }

    private void OnLogEntryAdded(object? sender, string entry)
    {
        var dispatcher = System.Windows.Application.Current.Dispatcher;

        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            TryTrackHostHeartbeat(entry);

            LogEntries.Add(entry);

            while (LogEntries.Count > 500)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }

    private void TryTrackHostHeartbeat(string entry)
    {
        var markerIndex = entry.IndexOf("HostHeartbeat|", StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return;
        }

        var payload = entry[(markerIndex + "HostHeartbeat|".Length)..];
        var parts = payload.Split('|');

        if (parts.Length < 4)
        {
            return;
        }

        if (DateTimeOffset.TryParse(parts[0], out var timestamp))
        {
            _lastHostHeartbeatUtc = timestamp;
        }
    }

    private void OnHostHeartbeatTimerTick(object? sender, EventArgs e)
    {
        if (!IsExporting)
        {
            return;
        }

        if (_lastHostHeartbeatUtc is null)
        {
            HostActivityIndicatorBrush = "#F59E0B";
            HostActivityStatusText = "Waiting for first host heartbeat...";
            return;
        }

        var age = DateTimeOffset.UtcNow - _lastHostHeartbeatUtc.Value;
        var ageSeconds = (int)Math.Max(0, age.TotalSeconds);

        if (ageSeconds <= 15)
        {
            HostActivityIndicatorBrush = "#16A34A";
            HostActivityStatusText = $"Host active (last heartbeat {ageSeconds}s ago).";
            return;
        }

        if (ageSeconds <= 60)
        {
            HostActivityIndicatorBrush = "#F59E0B";
            HostActivityStatusText = $"Host heartbeat delayed ({ageSeconds}s).";
            return;
        }

        HostActivityIndicatorBrush = "#DC2626";
        HostActivityStatusText = $"No host heartbeat for {ageSeconds}s. Consider cancel/retry if this persists.";
    }

    private async Task<string?> WriteFailureDiagnosticsAsync(Exception exception, CancellationToken cancellationToken)
    {
        var diagnosticsContent = BuildFailureDiagnosticsContent(exception);

        try
        {
            if (!string.IsNullOrWhiteSpace(OutputDirectory))
            {
                var reportsDirectory = Path.Combine(OutputDirectory, "Export", "Reports");
                Directory.CreateDirectory(reportsDirectory);
                var diagnosticsPath = Path.Combine(reportsDirectory, "EXPORT_FAILURE.log");
                await File.WriteAllTextAsync(diagnosticsPath, diagnosticsContent, cancellationToken).ConfigureAwait(false);
                return diagnosticsPath;
            }
        }
        catch (Exception logException)
        {
            _logCollector.Add($"Failed to write failure diagnostics: {logException.Message}");
        }

        try
        {
            var fallbackDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TiaProjectExporter",
                "FailureDiagnostics");
            Directory.CreateDirectory(fallbackDirectory);
            var fallbackPath = Path.Combine(fallbackDirectory, $"export-failure-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
            await File.WriteAllTextAsync(fallbackPath, diagnosticsContent, cancellationToken).ConfigureAwait(false);
            return fallbackPath;
        }
        catch (Exception fallbackException)
        {
            _logCollector.Add($"Failed to write fallback diagnostics: {fallbackException.Message}");
            return null;
        }
    }

    private string BuildFailureDiagnosticsContent(Exception exception)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Export Failure Diagnostics");
        builder.AppendLine();
        builder.AppendLine($"Timestamp (UTC): {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Version: {AppVersion}");
        builder.AppendLine($"ProjectPath: {ProjectPath}");
        builder.AppendLine($"OutputDirectory: {OutputDirectory}");
        builder.AppendLine($"TiaOverridePath: {TiaInstallationPathOverride}");
        builder.AppendLine();
        builder.AppendLine("## Exception");
        builder.AppendLine(exception.ToString());
        builder.AppendLine();
        builder.AppendLine("## UI Log Snapshot");

        foreach (var entry in _logCollector.Snapshot)
        {
            builder.AppendLine(entry);
        }

        return builder.ToString();
    }
}
