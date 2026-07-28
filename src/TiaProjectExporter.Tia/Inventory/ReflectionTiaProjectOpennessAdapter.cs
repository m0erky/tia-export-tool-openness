using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Logging;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Tia.Inventory.Extraction;

namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Reflection-based Siemens Openness adapter that safely probes runtime availability.
/// </summary>
public sealed class ReflectionTiaProjectOpennessAdapter : ITiaProjectOpennessAdapter
{
    private readonly ITiaInstallationDiscoveryService _installationDiscoveryService;
    private readonly IReadOnlyList<ITiaDomainExtractor> _domainExtractors;
    private readonly ILogger<ReflectionTiaProjectOpennessAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReflectionTiaProjectOpennessAdapter"/> class.
    /// </summary>
    public ReflectionTiaProjectOpennessAdapter(
        ITiaInstallationDiscoveryService installationDiscoveryService,
        IEnumerable<ITiaDomainExtractor> domainExtractors,
        ILogger<ReflectionTiaProjectOpennessAdapter> logger)
    {
        _installationDiscoveryService = installationDiscoveryService;
        _domainExtractors = domainExtractors.ToArray();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TiaProjectTraversalResult> TraverseAsync(
        string projectPath,
        string? tiaInstallationPathOverride,
        TiaTraversalDetailLevel detailLevel,
        CancellationToken cancellationToken,
        IReadOnlyCollection<ExportDomain>? includedDomains = null)
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
        var preferredInstallation = ResolvePreferredInstallation(installations, tiaInstallationPathOverride);

        if (preferredInstallation is null)
        {
            issues.Add(new ExportIssue(
                "OpennessRuntime",
                "No supported TIA installation with Openness runtime metadata was detected.",
                "Install TIA Portal V18, V19, or V20 and verify Openness components are present. You can also set a manual installation path override in the UI."));

            return new TiaProjectTraversalResult(
                ProjectName: Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath: projectPath,
                Objects: objects,
                Issues: issues);
        }

        var assemblyPath = OpennessRuntimeLocator.ResolveEngineeringAssemblyPath(preferredInstallation.InstallPath!);

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

    private static DiscoveredTiaPortalInstallation? ResolvePreferredInstallation(
        IReadOnlyList<DiscoveredTiaPortalInstallation> discoveredInstallations,
        string? tiaInstallationPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(tiaInstallationPathOverride))
        {
            var normalized = tiaInstallationPathOverride.Trim().Trim('"');
            var inferredVersion = InferVersionFromPath(normalized);
            return new DiscoveredTiaPortalInstallation(
                inferredVersion,
                $"Manual TIA Override ({inferredVersion})",
                normalized,
                OpennessAvailable: true);
        }

        return discoveredInstallations
            .Where(installation => installation.OpennessAvailable && !string.IsNullOrWhiteSpace(installation.InstallPath))
            .OrderByDescending(installation => installation.Version)
            .FirstOrDefault();
    }

    private static TiaPortalVersion InferVersionFromPath(string installPath)
    {
        if (installPath.Contains("V18", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains("Portal V18", StringComparison.OrdinalIgnoreCase))
        {
            return TiaPortalVersion.V18;
        }

        if (installPath.Contains("V19", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains("Portal V19", StringComparison.OrdinalIgnoreCase))
        {
            return TiaPortalVersion.V19;
        }

        return TiaPortalVersion.V20;
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
                DescribeException(exception)));
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

        var openMethods = projects.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method =>
                method.Name == "Open" &&
                method.GetParameters().Length == 1)
            .ToArray();

        var openMethod = openMethods.FirstOrDefault(method => method.GetParameters()[0].ParameterType == typeof(FileInfo))
            ?? openMethods.FirstOrDefault();

        if (openMethod is null)
        {
            return null;
        }

        var parameterType = openMethod.GetParameters()[0].ParameterType;
        object argument = parameterType == typeof(FileInfo)
            ? new FileInfo(projectPath)
            : projectPath;

        try
        {
            return openMethod.Invoke(projects, [argument]);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Opening project failed for path '{projectPath}'. {DescribeException(exception)}",
                exception);
        }
    }

    private static string DescribeException(Exception exception)
    {
        var current = exception;
        var segments = new List<string>();

        while (current is not null)
        {
            var segment = string.IsNullOrWhiteSpace(current.Message)
                ? current.GetType().Name
                : $"{current.GetType().Name}: {current.Message}";
            segments.Add(segment);
            current = current.InnerException;
        }

        return string.Join(" | Inner: ", segments);
    }

    private void TraverseProjectRoot(
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

            TraverseSoftwareGraphForDevice(device, deviceName, objects, issues, cancellationToken);

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

    private void TraverseSoftwareGraphForDevice(
        object? device,
        string deviceName,
        ICollection<TiaProjectObjectNode> objects,
        ICollection<ExportIssue> issues,
        CancellationToken cancellationToken)
    {
        if (device is null)
        {
            return;
        }

        var queue = new Queue<(object Node, string Path, int Depth)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var discoveredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((device, $"Project/Devices/{deviceName}", 2));

        var discoveredCount = 0;
        const int MaxNodes = 5000;

        while (queue.Count > 0 && discoveredCount < MaxNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, currentPath, depth) = queue.Dequeue();

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var child in EnumerateChildObjects(current))
            {
                if (child is null || visited.Contains(child))
                {
                    continue;
                }

                var childName = TryReadString(child, "Name")
                    ?? TryReadString(child, "DisplayName")
                    ?? child.GetType().Name;

                var childPath = $"{currentPath}/{childName}";
                var extractedNode = TryExtractNode(child, childPath, depth + 1);

                if (extractedNode is not null)
                {
                    var dedupKey = $"{extractedNode.ObjectType}|{extractedNode.QualifiedPath}";

                    if (!discoveredKeys.Add(dedupKey))
                    {
                        continue;
                    }

                    objects.Add(extractedNode);

                    discoveredCount++;
                }

                if (depth < 6)
                {
                    queue.Enqueue((child, childPath, depth + 1));
                }
            }
        }

        if (discoveredCount == 0)
        {
            issues.Add(new ExportIssue(
                "OpennessTraversal",
                $"No software-level objects discovered for device '{deviceName}'.",
                "Reflection graph traversal did not identify block/tag/HMI-like objects for this device yet."));
        }
    }

    private TiaProjectObjectNode? TryExtractNode(object runtimeNode, string qualifiedPath, int depth)
    {
        var runtimeTypeName = runtimeNode.GetType().Name;

        foreach (var extractor in _domainExtractors)
        {
            if (!extractor.CanHandle(runtimeTypeName))
            {
                continue;
            }

            var extractedNode = extractor.TryExtract(runtimeNode, qualifiedPath, depth);

            if (extractedNode is not null)
            {
                return MarkTypedExtraction(extractedNode, extractor);
            }
        }

        return TryFallbackExtraction(runtimeNode, runtimeTypeName, qualifiedPath, depth);
    }

    private static TiaProjectObjectNode MarkTypedExtraction(TiaProjectObjectNode node, ITiaDomainExtractor extractor)
    {
        var metadata = new Dictionary<string, string>(node.Metadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["ExtractedByTypedExtractor"] = "true",
            ["TypedExtractor"] = extractor.GetType().Name,
            ["FallbackReflectionUsed"] = "false"
        };

        return node with { Metadata = metadata };
    }

    private static TiaProjectObjectNode? TryFallbackExtraction(
        object runtimeNode,
        string runtimeTypeName,
        string qualifiedPath,
        int depth)
    {
        var classification = FallbackRuntimeClassifier.Classify(runtimeTypeName, qualifiedPath);
        if (classification is null)
        {
            return null;
        }

        var name = TryReadString(runtimeNode, "Name")
            ?? TryReadString(runtimeNode, "DisplayName")
            ?? runtimeTypeName;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, classification.ObjectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = classification.Domain,
            ["ExtractionStrategy"] = "ReflectionFallback",
            ["ExtractedByTypedExtractor"] = "false",
            ["FallbackReflectionUsed"] = "true"
        };

        return new TiaProjectObjectNode(
            ObjectType: classification.ObjectType,
            Name: name,
            QualifiedPath: qualifiedPath,
            Depth: depth,
            Metadata: metadata);
    }

    private static IEnumerable<object> EnumerateChildObjects(object parent)
    {
        var properties = parent.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            object? value;

            try
            {
                value = property.GetValue(parent);
            }
            catch
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (IsSimpleValue(value.GetType()))
            {
                continue;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                {
                    if (item is null || IsSimpleValue(item.GetType()))
                    {
                        continue;
                    }

                    yield return item;
                }

                continue;
            }

            yield return value;
        }
    }

    private static bool IsSimpleValue(Type type)
    {
        if (type.IsPrimitive || type.IsEnum)
        {
            return true;
        }

        return type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    private static string? TryReadString(object node, string propertyName) =>
        ReflectionNodeIntrospection.TryReadString(node, propertyName);

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

}
