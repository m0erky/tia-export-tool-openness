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

    private static void TraverseSoftwareGraphForDevice(
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
                var objectType = ClassifyNodeType(child.GetType().Name);

                if (objectType is not null)
                {
                    var metadata = BuildMetadata(child);

                    objects.Add(new TiaProjectObjectNode(
                        ObjectType: objectType,
                        Name: childName,
                        QualifiedPath: childPath,
                        Depth: depth + 1,
                        Metadata: metadata));

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

            if (value is IEnumerable enumerable and value is not string)
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

    private static string? ClassifyNodeType(string runtimeTypeName)
    {
        if (runtimeTypeName.Contains("OB", StringComparison.OrdinalIgnoreCase))
        {
            return "OB";
        }

        if (runtimeTypeName.Contains("FB", StringComparison.OrdinalIgnoreCase))
        {
            return "FB";
        }

        if (runtimeTypeName.Contains("FC", StringComparison.OrdinalIgnoreCase))
        {
            return "FC";
        }

        if (runtimeTypeName.Contains("DB", StringComparison.OrdinalIgnoreCase))
        {
            return "DB";
        }

        if (runtimeTypeName.Contains("Block", StringComparison.OrdinalIgnoreCase))
        {
            return "Block";
        }

        if (runtimeTypeName.Contains("Tag", StringComparison.OrdinalIgnoreCase))
        {
            return "Tag";
        }

        if (runtimeTypeName.Contains("Type", StringComparison.OrdinalIgnoreCase)
            || runtimeTypeName.Contains("UDT", StringComparison.OrdinalIgnoreCase))
        {
            return "UDT";
        }

        if (runtimeTypeName.Contains("Screen", StringComparison.OrdinalIgnoreCase))
        {
            return "Screen";
        }

        if (runtimeTypeName.Contains("Faceplate", StringComparison.OrdinalIgnoreCase))
        {
            return "Faceplate";
        }

        if (runtimeTypeName.Contains("Hmi", StringComparison.OrdinalIgnoreCase))
        {
            return "HMI";
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> BuildMetadata(object node)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RuntimeType"] = node.GetType().FullName ?? node.GetType().Name
        };

        AddMetadataIfPresent(node, metadata, "Comment", "Comment");
        AddMetadataIfPresent(node, metadata, "Title", "Title");
        AddMetadataIfPresent(node, metadata, "Description", "Description");
        AddMetadataIfPresent(node, metadata, "Text", "Text");
        AddMetadataIfPresent(node, metadata, "TextDe", "Text_de-DE");
        AddMetadataIfPresent(node, metadata, "TextEn", "Text_en-US");
        AddMetadataIfPresent(node, metadata, "CommentDe", "Comment_de-DE");
        AddMetadataIfPresent(node, metadata, "CommentEn", "Comment_en-US");

        var calls = ExtractNamedReferences(node, "Calls", "CalledBlocks", "ReferencedBlocks", "UsedBlocks");
        if (calls.Length > 0)
        {
            metadata["Calls"] = string.Join(", ", calls);
        }

        var dependencies = ExtractNamedReferences(node, "References", "Dependencies", "UsedTypes", "ReferencedTags");
        if (dependencies.Length > 0)
        {
            metadata["Dependencies"] = string.Join(", ", dependencies);
        }

        return metadata;
    }

    private static void AddMetadataIfPresent(object node, IDictionary<string, string> metadata, string propertyName, string metadataKey)
    {
        var value = TryReadString(node, propertyName);

        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[metadataKey] = value;
        }
    }

    private static string? TryReadString(object node, string propertyName)
    {
        try
        {
            var property = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            var value = property?.GetValue(node);
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string[] ExtractNamedReferences(object node, params string[] propertyNames)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var propertyName in propertyNames)
        {
            object? value;

            try
            {
                var property = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                value = property?.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (value is string stringValue)
            {
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    names.Add(stringValue.Trim());
                }

                continue;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var name = item is null ? null : TryReadString(item, "Name") ?? item.ToString();

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name.Trim());
                    }
                }

                continue;
            }

            var singleName = TryReadString(value, "Name") ?? value.ToString();
            if (!string.IsNullOrWhiteSpace(singleName))
            {
                names.Add(singleName.Trim());
            }
        }

        return names.ToArray();
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
