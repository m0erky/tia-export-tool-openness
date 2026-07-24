using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Logging;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Reflection-based Siemens Openness adapter that safely probes runtime availability.
/// </summary>
public sealed class ReflectionTiaProjectOpennessAdapter : ITiaProjectOpennessAdapter
{
    private static readonly IReadOnlyList<string> AssemblyCandidateDirectories =
    [
        string.Empty,
        "Bin",
        "PublicAPI",
        "PublicAPI\\V18",
        "PublicAPI\\V19",
        "PublicAPI\\V20"
    ];

    private readonly ITiaInstallationDiscoveryService _installationDiscoveryService;
    private readonly ILogger<ReflectionTiaProjectOpennessAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReflectionTiaProjectOpennessAdapter"/> class.
    /// </summary>
    public ReflectionTiaProjectOpennessAdapter(
        ITiaInstallationDiscoveryService installationDiscoveryService,
        ILogger<ReflectionTiaProjectOpennessAdapter> logger)
    {
        _installationDiscoveryService = installationDiscoveryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TiaProjectTraversalResult> TraverseAsync(string projectPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var issues = new List<ExportIssue>();
        var objects = new List<TiaProjectObjectNode>
        {
            new(
                ObjectType: "Project",
                Name: Path.GetFileNameWithoutExtension(projectPath),
                QualifiedPath: "Project",
                Depth: 0,
                Metadata: new Dictionary<string, string>
                {
                    ["SourcePath"] = projectPath
                })
        };

        if (!OperatingSystem.IsWindows())
        {
            issues.Add(new ExportIssue(
                "OpennessRuntime",
                "Siemens Openness runtime probing is only supported on Windows.",
                "Run the exporter on Windows with TIA Portal V18, V19, or V20 installed."));

            return new TiaProjectTraversalResult(
                ProjectName: Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath: projectPath,
                Objects: objects,
                Issues: issues);
        }

        var installations = await _installationDiscoveryService.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var preferredInstallation = installations
            .Where(installation => installation.OpennessAvailable && !string.IsNullOrWhiteSpace(installation.InstallPath))
            .OrderByDescending(installation => installation.Version)
            .FirstOrDefault();

        if (preferredInstallation is null)
        {
            issues.Add(new ExportIssue(
                "OpennessRuntime",
                "No supported TIA installation with Openness runtime metadata was detected.",
                "Install TIA Portal V18, V19, or V20 and verify Openness components are present."));

            return new TiaProjectTraversalResult(
                ProjectName: Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath: projectPath,
                Objects: objects,
                Issues: issues);
        }

        var assemblyPath = ResolveEngineeringAssemblyPath(preferredInstallation.InstallPath!);

        if (assemblyPath is null)
        {
            issues.Add(new ExportIssue(
                "OpennessRuntime",
                "Siemens.Engineering assembly could not be located in the selected installation.",
                $"InstallPath: {preferredInstallation.InstallPath}"));

            return new TiaProjectTraversalResult(
                ProjectName: Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath: projectPath,
                Objects: objects,
                Issues: issues);
        }

        ProbeRuntimeAndTraverseProject(
            preferredInstallation,
            assemblyPath,
            projectPath,
            objects,
            issues,
            cancellationToken);

        return new TiaProjectTraversalResult(
            ProjectName: Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath: projectPath,
            Objects: objects,
            Issues: issues);
    }

    private void ProbeRuntimeAndTraverseProject(
        DiscoveredTiaPortalInstallation installation,
        string assemblyPath,
        string projectPath,
        ICollection<TiaProjectObjectNode> objects,
        ICollection<ExportIssue> issues,
        CancellationToken cancellationToken)
    {
        object? tiaPortal = null;
        object? project = null;

        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            var tiaPortalType = assembly.GetType("Siemens.Engineering.TiaPortal");
            var modeType = assembly.GetType("Siemens.Engineering.TiaPortalMode");

            if (tiaPortalType is null || modeType is null)
            {
                issues.Add(new ExportIssue(
                    "OpennessRuntime",
                    "Loaded Siemens.Engineering assembly does not expose expected TiaPortal types.",
                    $"Assembly: {assembly.FullName}"));
                return;
            }

            var mode = ResolvePortalMode(modeType);
            tiaPortal = Activator.CreateInstance(tiaPortalType, mode);

            if (tiaPortal is null)
            {
                issues.Add(new ExportIssue(
                    "OpennessRuntime",
                    "Unable to create Siemens TiaPortal runtime instance.",
                    "Activator returned null for Siemens.Engineering.TiaPortal."));
                return;
            }

            objects.Add(new TiaProjectObjectNode(
                ObjectType: "OpennessRuntime",
                Name: installation.DisplayName,
                QualifiedPath: "Project/OpennessRuntime",
                Depth: 1,
                Metadata: new Dictionary<string, string>
                {
                    ["Version"] = installation.Version.ToString(),
                    ["AssemblyPath"] = assemblyPath,
                    ["AssemblyName"] = assembly.GetName().Name ?? "Unknown"
                }));

            project = TryOpenProject(tiaPortal, projectPath);

            if (project is null)
            {
                issues.Add(new ExportIssue(
                    "OpennessTraversal",
                    "Could not open project through Siemens Openness runtime.",
                    "The selected project may require a different TIA version or access mode."));
                return;
            }

            TraverseProjectRoot(project, objects, issues, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed Siemens Openness traversal for project {ProjectPath}", projectPath);
            issues.Add(new ExportIssue(
                "OpennessTraversal",
                "Siemens Openness traversal failed.",
                exception.Message));
        }
        finally
        {
            TryCloseProject(project);
            TryDispose(project);
            TryDispose(tiaPortal);
        }
    }

    private static object ResolvePortalMode(Type modeType)
    {
        try
        {
            return Enum.Parse(modeType, "WithoutUserInterface", ignoreCase: true);
        }
        catch
        {
            var values = Enum.GetValues(modeType);
            return values.Length > 0
                ? values.GetValue(0)!
                : throw new InvalidOperationException("No TiaPortalMode values are available.");
        }
    }

    private static object? TryOpenProject(object tiaPortal, string projectPath)
    {
        var projectsProperty = tiaPortal.GetType().GetProperty("Projects", BindingFlags.Public | BindingFlags.Instance);
        var projects = projectsProperty?.GetValue(tiaPortal);

        if (projects is null)
        {
            return null;
        }

        var openMethod = projects.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                method.Name == "Open" &&
                method.GetParameters().Length == 1);

        if (openMethod is null)
        {
            return null;
        }

        var parameterType = openMethod.GetParameters()[0].ParameterType;
        object argument = parameterType == typeof(FileInfo)
            ? new FileInfo(projectPath)
            : projectPath;

        return openMethod.Invoke(projects, [argument]);
    }

    private static void TraverseProjectRoot(
        object project,
        ICollection<TiaProjectObjectNode> objects,
        ICollection<ExportIssue> issues,
        CancellationToken cancellationToken)
    {
        var projectName = project.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(project)?.ToString();

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            objects.Add(new TiaProjectObjectNode(
                ObjectType: "ProjectMetadata",
                Name: projectName,
                QualifiedPath: "Project/Metadata",
                Depth: 1,
                Metadata: new Dictionary<string, string>
                {
                    ["RuntimeType"] = project.GetType().FullName ?? "Unknown"
                }));
        }

        var devicesProperty = project.GetType().GetProperty("Devices", BindingFlags.Public | BindingFlags.Instance);
        var devicesValue = devicesProperty?.GetValue(project);

        if (devicesValue is not IEnumerable devices)
        {
            issues.Add(new ExportIssue(
                "OpennessTraversal",
                "Project device composition was not available through reflection.",
                "The runtime object model may differ for this TIA version."));
            return;
        }

        var deviceCount = 0;

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var deviceName = device?.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(device)?.ToString();

            if (string.IsNullOrWhiteSpace(deviceName))
            {
                continue;
            }

            var runtimeType = device?.GetType().FullName ?? "Unknown";

            objects.Add(new TiaProjectObjectNode(
                ObjectType: "Device",
                Name: deviceName,
                QualifiedPath: $"Project/Devices/{deviceName}",
                Depth: 2,
                Metadata: new Dictionary<string, string>
                {
                    ["RuntimeType"] = runtimeType
                }));

            deviceCount++;
        }

        if (deviceCount == 0)
        {
            issues.Add(new ExportIssue(
                "OpennessTraversal",
                "No devices were enumerated from the opened project.",
                "Device composition may be empty or further API adaptation is required."));
        }
    }

    private static void TryCloseProject(object? project)
    {
        if (project is null)
        {
            return;
        }

        var closeMethod = project.GetType().GetMethod(
            "Close",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        closeMethod?.Invoke(project, Array.Empty<object>());
    }

    private static void TryDispose(object? instance)
    {
        if (instance is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string? ResolveEngineeringAssemblyPath(string installPath)
    {
        foreach (var candidateDirectory in AssemblyCandidateDirectories)
        {
            var candidatePath = string.IsNullOrWhiteSpace(candidateDirectory)
                ? Path.Combine(installPath, "Siemens.Engineering.dll")
                : Path.Combine(installPath, candidateDirectory, "Siemens.Engineering.dll");

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }
}
