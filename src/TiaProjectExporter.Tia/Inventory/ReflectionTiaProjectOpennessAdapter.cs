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

        try
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
            var tiaPortalType = assembly.GetType("Siemens.Engineering.TiaPortal");
            var projectsType = assembly.GetType("Siemens.Engineering.Project");

            if (tiaPortalType is null || projectsType is null)
            {
                issues.Add(new ExportIssue(
                    "OpennessRuntime",
                    "Loaded Siemens.Engineering assembly does not expose expected public types.",
                    $"Assembly: {assembly.FullName}"));
            }
            else
            {
                objects.Add(new TiaProjectObjectNode(
                    ObjectType: "OpennessRuntime",
                    Name: preferredInstallation.DisplayName,
                    QualifiedPath: "Project/OpennessRuntime",
                    Depth: 1,
                    Metadata: new Dictionary<string, string>
                    {
                        ["Version"] = preferredInstallation.Version.ToString(),
                        ["AssemblyPath"] = assemblyPath,
                        ["AssemblyName"] = assembly.GetName().Name ?? "Unknown"
                    }));

                issues.Add(new ExportIssue(
                    "OpennessTraversal",
                    "Siemens.Engineering runtime probe succeeded but deep project traversal is not yet implemented.",
                    "Next step: instantiate TiaPortal and map devices, PLC software, HMI, network, and metadata objects."));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to load Siemens.Engineering assembly from {AssemblyPath}", assemblyPath);
            issues.Add(new ExportIssue(
                "OpennessRuntime",
                "Failed to load Siemens.Engineering assembly for traversal.",
                exception.Message));
        }

        return new TiaProjectTraversalResult(
            ProjectName: Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath: projectPath,
            Objects: objects,
            Issues: issues);
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
