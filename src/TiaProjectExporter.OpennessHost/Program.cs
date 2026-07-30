using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace TiaProjectExporter.OpennessHost;

internal static class Program
{
    private const int MaxTraversalDepth = 6;
    private const int MaxPlcTraversalDepth = 10;
    private const int MaxChildrenPerNode = 2000;
    private const int MaxItemsPerEnumerableProperty = 1000;
    private const int MaxScalarMetadataEntries = 128;
    private const int MaxScalarMetadataValueLength = 1024;
    private const int MaxPreviewCandidates = 600;
    private const int MaxPreviewFallbackDepth = 5;
    private const int MaxPreviewFallbackNodes = 240;
    private const int MaxExportXmlFileBytes = 2 * 1024 * 1024;
    private const int MaxExportXmlChars = 500_000;
    private const int MaxSourceTextChars = 250_000;
    private const int MaxXmlSourceParseChars = 1_000_000;
    private const int MaxSafetyLoginProbeNodes = 400;
    private const int MaxSafetyLoginQueueSize = 3000;
    private const int MaxSafetyLoginDepth = 3;
    private const int MaxSafetyLoginChildrenPerNode = 96;
    private const int MaxSafetyLoginFailureIssues = 24;
    private static readonly TimeSpan SlowPropertyThreshold = TimeSpan.FromSeconds(2);
    private static readonly string[] SafetyAdministrationTypeCandidates =
    [
        "Siemens.Engineering.Safety.SafetyAdministration",
        "Siemens.Engineering.Safety.Services.SafetyAdministration",
        "Siemens.Engineering.Safety.ISafetyAdministration",
        "Siemens.Engineering.Safety.Services.ISafetyAdministration"
    ];

    private static HeartbeatSession? _heartbeatSession;
    private static string? _safetyOfflineProgramPassword;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Any(argument => string.Equals(argument, "--health", StringComparison.OrdinalIgnoreCase)))
        {
            HostHealthResponse healthResponse;

            try
            {
                var options = HostOptions.Parse(args, requireProjectPath: false);
                healthResponse = ExecuteHealthCheck(options);
            }
            catch (Exception exception)
            {
                healthResponse = new HostHealthResponse
                {
                    Healthy = false,
                    Details =
                    [
                        "Health check failed before execution.",
                        DescribeException(exception)
                    ]
                };
            }

            WriteJson(healthResponse);
            return 0;
        }

        HostTraversalResponse response;

        try
        {
            var options = HostOptions.Parse(args);
            response = ExecuteTraversal(options);
        }
        catch (Exception exception)
        {
            response = new HostTraversalResponse
            {
                ProjectName = null,
                ProjectPath = string.Empty,
                Objects = new List<HostObjectNode>(),
                Issues =
                [
                    new HostIssue
                    {
                        Scope = "OpennessHost",
                        Message = "Openness host failed before traversal could start.",
                        Details = DescribeException(exception)
                    }
                ]
            };
        }

        WriteJson(response);
        return 0;
    }

    private static HostHealthResponse ExecuteHealthCheck(HostOptions options)
    {
        var details = new List<string>();

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            details.Add("Host health check supports Windows only.");
            return new HostHealthResponse { Healthy = false, Details = details };
        }

        TryInitializeSiemensResolver(details);

        var assemblyPath = ResolveEngineeringAssemblyPath(options.InstallPath);

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            details.Add("Siemens.Engineering.dll not found for provided installation path.");
            details.Add($"InstallPath: {options.InstallPath}");
            return new HostHealthResponse { Healthy = false, Details = details };
        }

        details.Add($"Siemens.Engineering.dll: {assemblyPath}");

        try
        {
            var engineeringAssembly = Assembly.LoadFrom(assemblyPath);
            details.Add($"Loaded assembly: {engineeringAssembly.FullName}");

            var contractCandidatePath = Path.Combine(Path.GetDirectoryName(assemblyPath) ?? string.Empty, "Siemens.Engineering.Contract.dll");

            if (File.Exists(contractCandidatePath))
            {
                var contractAssembly = Assembly.LoadFrom(contractCandidatePath);
                details.Add($"Loaded contract assembly: {contractAssembly.FullName}");
            }
            else
            {
                details.Add("Siemens.Engineering.Contract.dll not found beside Siemens.Engineering.dll.");
            }

            var tiaPortalType = engineeringAssembly.GetType("Siemens.Engineering.TiaPortal");
            var modeType = engineeringAssembly.GetType("Siemens.Engineering.TiaPortalMode");

            if (tiaPortalType is null || modeType is null)
            {
                details.Add("Expected Siemens types (TiaPortal/TiaPortalMode) not found.");
                return new HostHealthResponse { Healthy = false, Details = details };
            }

            details.Add("Required Siemens runtime types are available.");
            return new HostHealthResponse { Healthy = true, Details = details };
        }
        catch (Exception exception)
        {
            details.Add(DescribeException(exception));
            return new HostHealthResponse { Healthy = false, Details = details };
        }
    }

    private static HostTraversalResponse ExecuteTraversal(HostOptions options)
    {
        _heartbeatSession = new HeartbeatSession();
        _heartbeatSession.UpdatePhase("Startup", "Preparing traversal host");

        var issues = new List<HostIssue>();
        var objects = new List<HostObjectNode>
        {
            new HostObjectNode
            {
                ObjectType = "Project",
                Name = Path.GetFileNameWithoutExtension(options.ProjectPath),
                QualifiedPath = "Project",
                Depth = 0,
                Metadata = new Dictionary<string, string>
                {
                    ["SourcePath"] = options.ProjectPath,
                    ["ExtractionStrategy"] = "HostRoot"
                }
            }
        };

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessRuntime",
                Message = "Openness host is only supported on Windows.",
                Details = "Run exporter and host on a Windows machine with TIA installed."
            });

            return new HostTraversalResponse
            {
                ProjectName = Path.GetFileNameWithoutExtension(options.ProjectPath),
                ProjectPath = options.ProjectPath,
                Objects = objects,
                Issues = issues
            };
        }

        TryInitializeSiemensResolver();

        var assemblyPath = ResolveEngineeringAssemblyPath(options.InstallPath);
        _heartbeatSession.UpdatePhase("ResolveAssemblies", "Resolving Siemens.Engineering assembly");

        if (assemblyPath is null)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessRuntime",
                Message = "Siemens.Engineering.dll not found in configured installation path.",
                Details = $"InstallPath: {options.InstallPath}"
            });

            return new HostTraversalResponse
            {
                ProjectName = Path.GetFileNameWithoutExtension(options.ProjectPath),
                ProjectPath = options.ProjectPath,
                Objects = objects,
                Issues = issues
            };
        }

        object? tiaPortal = null;
        object? project = null;

        try
        {
            _heartbeatSession.UpdatePhase("LoadRuntime", "Loading Siemens runtime assemblies");
            var engineeringAssembly = Assembly.LoadFrom(assemblyPath);
            var tiaPortalType = engineeringAssembly.GetType("Siemens.Engineering.TiaPortal");
            var modeType = engineeringAssembly.GetType("Siemens.Engineering.TiaPortalMode");

            if (tiaPortalType is null || modeType is null)
            {
                issues.Add(new HostIssue
                {
                    Scope = "OpennessRuntime",
                    Message = "Loaded Siemens.Engineering assembly does not expose expected TiaPortal types.",
                    Details = engineeringAssembly.FullName
                });

                return BuildResult(options.ProjectPath, objects, issues);
            }

            var mode = ResolvePortalMode(modeType);
            _heartbeatSession.UpdatePhase("CreatePortal", "Creating TiaPortal instance");
            tiaPortal = Activator.CreateInstance(tiaPortalType, mode);

            if (tiaPortal is null)
            {
                issues.Add(new HostIssue
                {
                    Scope = "OpennessRuntime",
                    Message = "Failed to create Siemens.Engineering.TiaPortal instance.",
                    Details = "Activator returned null"
                });

                return BuildResult(options.ProjectPath, objects, issues);
            }

            objects.Add(new HostObjectNode
            {
                ObjectType = "OpennessRuntime",
                Name = "Siemens.Engineering",
                QualifiedPath = "Project/OpennessRuntime",
                Depth = 1,
                Metadata = new Dictionary<string, string>
                {
                    ["AssemblyPath"] = assemblyPath,
                    ["AssemblyName"] = engineeringAssembly.GetName().Name ?? "Unknown",
                    ["AssemblyVersion"] = engineeringAssembly.GetName().Version?.ToString() ?? "Unknown",
                    ["ExtractionStrategy"] = "HostRuntime"
                }
            });

            _heartbeatSession.UpdatePhase("OpenProject", "Opening TIA project");
            project = OpenProject(tiaPortal, options.ProjectPath);
            _safetyOfflineProgramPassword = options.SafetyOfflineProgramPassword;

            if (project is null)
            {
                issues.Add(new HostIssue
                {
                    Scope = "OpennessTraversal",
                    Message = "Could not open project through Siemens Openness runtime.",
                    Details = "Projects.Open returned null"
                });

                return BuildResult(options.ProjectPath, objects, issues);
            }

            _heartbeatSession.UpdatePhase("Traverse", "Walking project graph");
            var domainScope = TraversalDomainScope.FromRaw(options.IncludedDomains);

            if (!options.IsPreview && !string.IsNullOrWhiteSpace(options.SafetyOfflineProgramPassword))
            {
                TryLoginToSafetyOfflineProgram(project, issues);
            }

            if (options.IsPreview)
            {
                TraverseProjectPreview(project, objects, issues, domainScope);
            }
            else
            {
                TraverseProject(project, objects, issues, domainScope);
            }
        }
        catch (Exception exception)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessTraversal",
                Message = "Siemens Openness traversal failed in out-of-process host.",
                Details = DescribeException(exception)
            });
        }
        finally
        {
            TryCloseProject(project);
            TryDispose(project);
            TryDispose(tiaPortal);
            _safetyOfflineProgramPassword = null;
            _heartbeatSession?.Dispose();
            _heartbeatSession = null;
        }

        return BuildResult(options.ProjectPath, objects, issues);
    }

    private static HostTraversalResponse BuildResult(string projectPath, IReadOnlyList<HostObjectNode> objects, IReadOnlyList<HostIssue> issues)
    {
        return new HostTraversalResponse
        {
            ProjectName = Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath = projectPath,
            Objects = objects.ToList(),
            Issues = issues.ToList()
        };
    }

    private static object ResolvePortalMode(Type modeType)
    {
        try
        {
            return Enum.Parse(modeType, "WithoutUserInterface", true);
        }
        catch
        {
            var values = Enum.GetValues(modeType);
            if (values.Length == 0)
            {
                throw new InvalidOperationException("No TiaPortalMode values are available.");
            }

            return values.GetValue(0)!;
        }
    }

    private static object? OpenProject(object tiaPortal, string projectPath)
    {
        var projectsProperty = tiaPortal.GetType().GetProperty("Projects", BindingFlags.Public | BindingFlags.Instance);
        var projects = projectsProperty?.GetValue(tiaPortal);

        if (projects is null)
        {
            return null;
        }

        var openMethods = projects.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Open" && method.GetParameters().Length == 1)
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
            return openMethod.Invoke(projects, new[] { argument });
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Opening project failed for path '{projectPath}'. {DescribeException(exception)}", exception);
        }
    }

    private static void TraverseProject(
        object project,
        ICollection<HostObjectNode> objects,
        ICollection<HostIssue> issues,
        TraversalDomainScope domainScope)
    {
        _heartbeatSession?.UpdatePhase("TraverseProject", "Inspecting project root and devices");
        var projectName = TryReadString(project, "Name");

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            objects.Add(new HostObjectNode
            {
                ObjectType = "ProjectMetadata",
                Name = projectName!,
                QualifiedPath = "Project/Metadata",
                Depth = 1,
                Metadata = BuildNodeMetadata(project, "HostProjectMetadata", "Project/Metadata", issues)
            });
        }

        var devicesProperty = project.GetType().GetProperty("Devices", BindingFlags.Public | BindingFlags.Instance);
        var devicesValue = devicesProperty?.GetValue(project);

        if (devicesValue is not IEnumerable devices)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessTraversal",
                Message = "Project devices collection not available.",
                Details = "No enumerable Devices property."
            });
            return;
        }

        var deviceCount = 0;

        foreach (var device in devices)
        {
            if (device is null)
            {
                continue;
            }

            var deviceName = TryReadString(device, "Name") ?? TryReadString(device, "DisplayName") ?? "Device";
            _heartbeatSession?.UpdatePhase("TraverseDevice", $"Device: {deviceName}");
            var devicePath = $"Project/Devices/{deviceName}";

            objects.Add(new HostObjectNode
            {
                ObjectType = "Device",
                Name = deviceName,
                QualifiedPath = devicePath,
                Depth = 1,
                Metadata = BuildNodeMetadata(device, "HostDevice", devicePath, issues)
            });

            deviceCount++;

            if (domainScope.IncludeGenericSoftwareGraph)
            {
                TraverseSoftwareGraph(device, devicePath, objects, issues);
            }

            if (domainScope.IncludePlcFocusedGraph)
            {
                TraversePlcFocusedGraph(device, devicePath, objects, issues, domainScope);
            }
        }

        if (deviceCount == 0)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessTraversal",
                Message = "No devices were discovered in the project.",
                Details = "Project may be empty, protected, or incompatible."
            });
        }
    }

    private static void TraverseProjectPreview(
        object project,
        ICollection<HostObjectNode> objects,
        ICollection<HostIssue> issues,
        TraversalDomainScope domainScope)
    {
        if (!domainScope.AllowPreviewScan)
        {
            return;
        }

        _heartbeatSession?.UpdatePhase("TraversePreview", "Inspecting project root and top-level software areas");
        var diagnostics = new PreviewScanDiagnostics();

        var devicesProperty = project.GetType().GetProperty("Devices", BindingFlags.Public | BindingFlags.Instance);
        var devicesValue = devicesProperty?.GetValue(project);

        if (devicesValue is not IEnumerable devices)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessTraversal",
                Message = "Project devices collection not available.",
                Details = "No enumerable Devices property."
            });
            return;
        }

        foreach (var device in devices)
        {
            if (device is null)
            {
                continue;
            }

            var deviceName = TryReadString(device, "Name") ?? TryReadString(device, "DisplayName") ?? "Device";
            var devicePath = $"Project/Devices/{deviceName}";

            objects.Add(new HostObjectNode
            {
                ObjectType = "Device",
                Name = deviceName,
                QualifiedPath = devicePath,
                Depth = 1,
                Metadata = new Dictionary<string, string>
                {
                    ["RuntimeType"] = device.GetType().FullName ?? device.GetType().Name,
                    ["ExtractionStrategy"] = "HostPreview",
                    ["QualifiedPath"] = devicePath
                }
            });

            var entryCount = 0;
            foreach (var entry in ResolvePlcEntryPoints(device, devicePath, issues))
            {
                entryCount++;
                diagnostics.PlcEntryPointsDiscovered++;
                if (entryCount > 25)
                {
                    break;
                }

                AddPreviewNode(objects, entry.Node, entry.Path, 2);

                var previewQueue = new Queue<(object Node, string Path, int Depth)>();
                previewQueue.Enqueue((entry.Node, entry.Path, 2));

                var previewVisited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var previewCount = 0;
                var entryBlockCount = 0;
                var entryBlockGroupCount = 0;

                while (previewQueue.Count > 0 && previewCount < MaxPreviewCandidates)
                {
                    var current = previewQueue.Dequeue();

                    if (current.Depth >= 5)
                    {
                        continue;
                    }

                    foreach (var candidate in EnumeratePlcModelChildren(current.Node, current.Path, issues))
                    {
                        var key = $"{candidate.ObjectType}|{candidate.Path}";

                        if (!previewVisited.Add(key))
                        {
                            continue;
                        }

                        var candidatePath = string.IsNullOrWhiteSpace(candidate.Path)
                            ? current.Path
                            : candidate.Path!;

                        var candidateObjectType = candidate.ObjectType ?? ClassifyPlcObjectType(candidate.Node, candidatePath);

                        if (!domainScope.IsCandidateAllowed(candidateObjectType, candidatePath))
                        {
                            continue;
                        }

                        TrackPreviewDiagnostics(candidateObjectType, candidatePath, diagnostics, ref entryBlockCount, ref entryBlockGroupCount);

                        AddPreviewNode(objects, candidate.Node, candidatePath, current.Depth + 1, candidateObjectType, candidate.Strategy);
                        previewCount++;

                        if (candidateObjectType is "BlockGroup" or "TagTableGroup" or "TypeGroup")
                        {
                            previewQueue.Enqueue((candidate.Node, candidatePath, current.Depth + 1));
                        }

                        if (previewCount >= MaxPreviewCandidates)
                        {
                            break;
                        }
                    }
                }

                if (domainScope.IncludePlcFocusedGraph && entryBlockCount == 0)
                {
                    diagnostics.BlockFallbackActivations++;
                    var fallbackResult = DiscoverPreviewBlocksFallback(entry.Node, entry.Path, previewVisited, objects, issues, diagnostics, domainScope);
                    entryBlockCount += fallbackResult.BlockCount;
                    entryBlockGroupCount += fallbackResult.BlockGroupCount;
                }

                if (previewCount >= MaxPreviewCandidates)
                {
                    diagnostics.EntryLimitHits++;
                }
            }
        }

        AddPreviewDiagnosticsMetadata(objects, diagnostics);
    }

    private static PreviewFallbackResult DiscoverPreviewBlocksFallback(
        object entryNode,
        string entryPath,
        ISet<string> previewVisited,
        ICollection<HostObjectNode> objects,
        ICollection<HostIssue> issues,
        PreviewScanDiagnostics diagnostics,
        TraversalDomainScope domainScope)
    {
        _heartbeatSession?.UpdatePhase("TraversePreviewFallback", entryPath);

        var queue = new Queue<(object Node, string Path, int Depth)>();
        queue.Enqueue((entryNode, entryPath, 2));

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var examinedNodes = 0;
        var blockCount = 0;
        var blockGroupCount = 0;

        while (queue.Count > 0 && examinedNodes < MaxPreviewFallbackNodes)
        {
            var current = queue.Dequeue();

            if (current.Depth > MaxPreviewFallbackDepth)
            {
                continue;
            }

            if (!visited.Add(current.Node))
            {
                continue;
            }

            foreach (var child in EnumeratePlcChildObjects(current.Node, current.Path, issues))
            {
                examinedNodes++;
                diagnostics.BlockFallbackNodesVisited++;

                if (examinedNodes > MaxPreviewFallbackNodes)
                {
                    break;
                }

                var childName = TryReadString(child, "Name") ?? TryReadString(child, "DisplayName") ?? child.GetType().Name;
                var childPath = $"{current.Path}/{childName}";
                var objectType = ClassifyPlcObjectType(child, childPath);
                var previewKey = $"{objectType}|{childPath}";
                var relevant = IsPreviewBlockRelevant(objectType, childPath);

                if (!domainScope.IsCandidateAllowed(objectType, childPath))
                {
                    relevant = false;
                }

                if (relevant && previewVisited.Add(previewKey))
                {
                    AddPreviewNode(objects, child, childPath, current.Depth + 1, objectType, "HostPreviewBlockFallback");
                    TrackPreviewDiagnostics(objectType, childPath, diagnostics, ref blockCount, ref blockGroupCount);
                }

                if (ShouldTraversePreviewFallbackChild(child, childPath, objectType))
                {
                    queue.Enqueue((child, childPath, current.Depth + 1));
                }
            }
        }

        return new PreviewFallbackResult(blockCount, blockGroupCount);
    }

    private static bool IsPreviewBlockRelevant(string objectType, string qualifiedPath)
    {
        if (objectType is "OB" or "FB" or "FC" or "DB" or "InstanceDB" or "BlockGroup")
        {
            return true;
        }

        return qualifiedPath.Contains("/Blocks/", StringComparison.OrdinalIgnoreCase)
            || qualifiedPath.Contains("/BlockGroup", StringComparison.OrdinalIgnoreCase)
            || qualifiedPath.Contains("/Groups/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldTraversePreviewFallbackChild(object child, string path, string objectType)
    {
        if (objectType == "BlockGroup")
        {
            return true;
        }

        if (IsPreviewBlockRelevant(objectType, path))
        {
            return false;
        }

        var candidate = $"{child.GetType().FullName} {child.GetType().Name} {path}";
        return ContainsAny(candidate, "Plc", "Software", "Program", "Block", "Group", "CompileUnit", "Logic");
    }

    private static void TrackPreviewDiagnostics(
        string objectType,
        string qualifiedPath,
        PreviewScanDiagnostics diagnostics,
        ref int blockCount,
        ref int blockGroupCount)
    {
        if (objectType == "BlockGroup" || qualifiedPath.Contains("/BlockGroup", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.BlockGroupsDiscovered++;
            blockGroupCount++;
            return;
        }

        switch (objectType)
        {
            case "OB":
                diagnostics.ObBlocksDiscovered++;
                blockCount++;
                break;
            case "FB":
                diagnostics.FbBlocksDiscovered++;
                blockCount++;
                break;
            case "FC":
                diagnostics.FcBlocksDiscovered++;
                blockCount++;
                break;
            case "DB":
            case "InstanceDB":
                diagnostics.DbBlocksDiscovered++;
                blockCount++;
                break;
        }
    }

    private static void AddPreviewDiagnosticsMetadata(ICollection<HostObjectNode> objects, PreviewScanDiagnostics diagnostics)
    {
        HostObjectNode? projectRoot = null;
        foreach (var node in objects)
        {
            if (node.Depth == 0 && string.Equals(node.QualifiedPath, "Project", StringComparison.OrdinalIgnoreCase))
            {
                projectRoot = node;
                break;
            }
        }

        if (projectRoot is null)
        {
            return;
        }

        projectRoot.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        projectRoot.Metadata["PreviewDiagnostics.PlcEntryPoints"] = diagnostics.PlcEntryPointsDiscovered.ToString();
        projectRoot.Metadata["PreviewDiagnostics.BlockGroups"] = diagnostics.BlockGroupsDiscovered.ToString();
        projectRoot.Metadata["PreviewDiagnostics.OB"] = diagnostics.ObBlocksDiscovered.ToString();
        projectRoot.Metadata["PreviewDiagnostics.FB"] = diagnostics.FbBlocksDiscovered.ToString();
        projectRoot.Metadata["PreviewDiagnostics.FC"] = diagnostics.FcBlocksDiscovered.ToString();
        projectRoot.Metadata["PreviewDiagnostics.DB"] = diagnostics.DbBlocksDiscovered.ToString();
        projectRoot.Metadata["PreviewDiagnostics.FallbackActivations"] = diagnostics.BlockFallbackActivations.ToString();
        projectRoot.Metadata["PreviewDiagnostics.FallbackNodesVisited"] = diagnostics.BlockFallbackNodesVisited.ToString();
        projectRoot.Metadata["PreviewDiagnostics.PreviewLimitHits"] = diagnostics.EntryLimitHits.ToString();
    }

    private static void AddPreviewNode(
        ICollection<HostObjectNode> objects,
        object node,
        string path,
        int depth,
        string? objectTypeOverride = null,
        string? strategyOverride = null)
    {
        var objectType = string.IsNullOrWhiteSpace(objectTypeOverride)
            ? ClassifyPlcObjectType(node, path)
            : objectTypeOverride!;
        var name = TryReadString(node, "Name") ?? TryReadString(node, "DisplayName") ?? node.GetType().Name;

        objects.Add(new HostObjectNode
        {
            ObjectType = objectType,
            Name = name,
            QualifiedPath = path,
            Depth = depth,
            Metadata = new Dictionary<string, string>
            {
                ["RuntimeType"] = node.GetType().FullName ?? node.GetType().Name,
                ["ExtractionStrategy"] = strategyOverride ?? "HostPreview",
                ["QualifiedPath"] = path
            }
        });
    }

    private static void TraverseSoftwareGraph(object root, string rootPath, ICollection<HostObjectNode> objects, ICollection<HostIssue> issues)
    {
        _heartbeatSession?.UpdatePhase("TraverseSoftware", $"Root: {rootPath}");
        var queue = new Queue<(object Node, string Path, int Depth)>();
        queue.Enqueue((root, rootPath, 1));

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredCount = 0;
        var recursivePathSkips = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.Depth <= 3)
            {
                _heartbeatSession?.UpdatePhase("TraverseQueue", $"{current.Path} (depth {current.Depth})");
            }

            if (current.Depth > MaxTraversalDepth)
            {
                continue;
            }

            var childrenAddedForNode = 0;
            var nodeTraversalTimer = Stopwatch.StartNew();

            foreach (var child in EnumerateChildObjects(current.Node, current.Path, issues))
            {
                if (childrenAddedForNode >= MaxChildrenPerNode)
                {
                    issues.Add(new HostIssue
                    {
                        Scope = "OpennessTraversal",
                        Message = "Node child limit reached; remaining children were skipped.",
                        Details = $"Node: {current.Path}; Limit: {MaxChildrenPerNode}"
                    });
                    break;
                }

                var childName = TryReadString(child, "Name") ?? TryReadString(child, "DisplayName") ?? child.GetType().Name;
                var childPath = $"{current.Path}/{childName}";
                var childTypeName = child.GetType().Name;

                if (IsLikelyRecursiveHardwarePath(childPath))
                {
                    recursivePathSkips++;
                    if (recursivePathSkips <= 8)
                    {
                        issues.Add(new HostIssue
                        {
                            Scope = "OpennessTraversal",
                            Message = "Skipping likely recursive hardware path expansion.",
                            Details = $"Path: {childPath}"
                        });
                    }

                    continue;
                }

                var objectType = ClassifyObjectType(childTypeName, childPath);
                var dedupKey = $"{objectType}|{childPath}";

                if (!visited.Add(dedupKey))
                {
                    continue;
                }

                objects.Add(new HostObjectNode
                {
                    ObjectType = objectType,
                    Name = childName,
                    QualifiedPath = childPath,
                    Depth = current.Depth + 1,
                    Metadata = BuildNodeMetadata(child, "HostReflection", childPath, issues)
                });

                discoveredCount++;
                childrenAddedForNode++;
                queue.Enqueue((child, childPath, current.Depth + 1));

                if (nodeTraversalTimer.Elapsed > TimeSpan.FromSeconds(30))
                {
                    issues.Add(new HostIssue
                    {
                        Scope = "OpennessTraversal",
                        Message = "Node traversal watchdog limit reached; remaining child discovery was skipped.",
                        Details = $"Node: {current.Path}; Elapsed: {nodeTraversalTimer.Elapsed:c}"
                    });
                    break;
                }
            }
        }

        if (discoveredCount == 0)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessTraversal",
                Message = "No software-level objects discovered during traversal.",
                Details = "Host reflection walk found no child nodes beyond device root."
            });
        }
    }

    private static void TraversePlcFocusedGraph(
        object deviceRoot,
        string devicePath,
        ICollection<HostObjectNode> objects,
        ICollection<HostIssue> issues,
        TraversalDomainScope domainScope)
    {
        var entryPoints = ResolvePlcEntryPoints(deviceRoot, devicePath, issues).ToArray();

        if (entryPoints.Length == 0)
        {
            return;
        }

        _heartbeatSession?.UpdatePhase("TraversePlc", $"Device root: {devicePath}");

        var queue = new Queue<(object Node, string Path, int Depth)>();
        foreach (var entry in entryPoints)
        {
            queue.Enqueue((entry.Node, entry.Path, 1));
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recursivePathSkips = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.Depth > MaxPlcTraversalDepth)
            {
                continue;
            }

            _heartbeatSession?.UpdatePhase("TraversePlcQueue", $"{current.Path} (depth {current.Depth})");

            var childrenAdded = 0;

            foreach (var candidate in EnumeratePlcTraversalCandidates(current.Node, current.Path, issues))
            {
                if (childrenAdded >= MaxChildrenPerNode)
                {
                    issues.Add(new HostIssue
                    {
                        Scope = "OpennessTraversal",
                        Message = "PLC traversal node child limit reached; remaining children were skipped.",
                        Details = $"Node: {current.Path}; Limit: {MaxChildrenPerNode}"
                    });
                    break;
                }

                var child = candidate.Node;
                var childName = TryReadString(child, "Name") ?? TryReadString(child, "DisplayName") ?? child.GetType().Name;
                var childPath = string.IsNullOrWhiteSpace(candidate.Path)
                    ? $"{current.Path}/{childName}"
                    : candidate.Path!;

                if (IsLikelyRecursiveHardwarePath(childPath))
                {
                    recursivePathSkips++;
                    if (recursivePathSkips <= 8)
                    {
                        issues.Add(new HostIssue
                        {
                            Scope = "OpennessTraversal",
                            Message = "Skipping likely recursive hardware path expansion in PLC traversal.",
                            Details = $"Path: {childPath}"
                        });
                    }

                    continue;
                }

                var objectType = string.IsNullOrWhiteSpace(candidate.ObjectType)
                    ? ClassifyPlcObjectType(child, childPath)
                    : candidate.ObjectType!;

                if (!domainScope.IsCandidateAllowed(objectType, childPath))
                {
                    continue;
                }

                var dedupKey = $"{objectType}|{childPath}";

                if (!visited.Add(dedupKey))
                {
                    continue;
                }

                if (!ContainsRuntimePathData(childPath, objectType))
                {
                    continue;
                }

                var hostNode = new HostObjectNode
                {
                    ObjectType = objectType,
                    Name = childName,
                    QualifiedPath = childPath,
                    Depth = current.Depth + 1,
                    Metadata = BuildNodeMetadata(child, candidate.Strategy ?? "HostReflectionPlcFocus", childPath, issues)
                };

                objects.Add(hostNode);

                EnrichWithDeepContent(child, objectType, childPath, hostNode.Metadata, issues);

                childrenAdded++;
                queue.Enqueue((child, childPath, current.Depth + 1));
            }
        }
    }

    private static IEnumerable<PlcTraversalCandidate> EnumeratePlcTraversalCandidates(object parent, string parentPath, ICollection<HostIssue> issues)
    {
        foreach (var explicitCandidate in EnumeratePlcModelChildren(parent, parentPath, issues))
        {
            yield return explicitCandidate;
        }

        foreach (var child in EnumeratePlcChildObjects(parent, parentPath, issues))
        {
            yield return new PlcTraversalCandidate(child, null, null, "HostReflectionPlcFocus");
        }
    }

    private static IEnumerable<PlcTraversalCandidate> EnumeratePlcModelChildren(object parent, string parentPath, ICollection<HostIssue> issues)
    {
        var parentTypeName = parent.GetType().FullName ?? parent.GetType().Name;

        var blockGroup = TryReadObjectProperty(parent, "BlockGroup");
        if (blockGroup is not null)
        {
            var blockGroupPath = $"{parentPath}/BlockGroup";
            yield return new PlcTraversalCandidate(blockGroup, blockGroupPath, "BlockGroup", "HostPlcModel");

            foreach (var block in EnumerateCollectionProperty(blockGroup, "Blocks", issues, blockGroupPath))
            {
                var blockName = TryReadString(block, "Name") ?? block.GetType().Name;
                var blockPath = $"{blockGroupPath}/Blocks/{blockName}";
                yield return new PlcTraversalCandidate(block, blockPath, ClassifyPlcObjectType(block, blockPath), "HostPlcModel");
            }

            foreach (var subgroup in EnumerateCollectionProperty(blockGroup, "Groups", issues, blockGroupPath))
            {
                var groupName = TryReadString(subgroup, "Name") ?? subgroup.GetType().Name;
                var groupPath = $"{blockGroupPath}/Groups/{groupName}";
                yield return new PlcTraversalCandidate(subgroup, groupPath, "BlockGroup", "HostPlcModel");
            }
        }

        var tagTableGroup = TryReadObjectProperty(parent, "TagTableGroup");
        if (tagTableGroup is not null)
        {
            var groupPath = $"{parentPath}/TagTableGroup";
            yield return new PlcTraversalCandidate(tagTableGroup, groupPath, "TagTableGroup", "HostPlcModel");

            foreach (var table in EnumerateCollectionProperty(tagTableGroup, "TagTables", issues, groupPath))
            {
                var tableName = TryReadString(table, "Name") ?? table.GetType().Name;
                var tablePath = $"{groupPath}/TagTables/{tableName}";
                yield return new PlcTraversalCandidate(table, tablePath, "TagTable", "HostPlcModel");
            }
        }

        var typeGroup = TryReadObjectProperty(parent, "TypeGroup") ?? TryReadObjectProperty(parent, "PlcTypeGroup");
        if (typeGroup is not null)
        {
            var groupPath = $"{parentPath}/TypeGroup";
            yield return new PlcTraversalCandidate(typeGroup, groupPath, "TypeGroup", "HostPlcModel");

            foreach (var type in EnumerateCollectionProperty(typeGroup, "Types", issues, groupPath))
            {
                var typeName = TryReadString(type, "Name") ?? type.GetType().Name;
                var typePath = $"{groupPath}/Types/{typeName}";
                yield return new PlcTraversalCandidate(type, typePath, ClassifyPlcObjectType(type, typePath), "HostPlcModel");
            }
        }

        foreach (var technology in EnumerateCollectionProperty(parent, "TechnologyObjects", issues, parentPath))
        {
            var technologyName = TryReadString(technology, "Name") ?? technology.GetType().Name;
            var technologyPath = $"{parentPath}/TechnologyObjects/{technologyName}";
            yield return new PlcTraversalCandidate(technology, technologyPath, "TechnologyObject", "HostPlcModel");
        }

        foreach (var source in EnumerateCollectionProperty(parent, "ExternalSources", issues, parentPath))
        {
            var sourceName = TryReadString(source, "Name") ?? source.GetType().Name;
            var sourcePath = $"{parentPath}/ExternalSources/{sourceName}";
            yield return new PlcTraversalCandidate(source, sourcePath, "Source", "HostPlcModel");
        }

        foreach (var source in EnumerateCollectionProperty(parent, "Sources", issues, parentPath))
        {
            var sourceName = TryReadString(source, "Name") ?? source.GetType().Name;
            var sourcePath = $"{parentPath}/Sources/{sourceName}";
            yield return new PlcTraversalCandidate(source, sourcePath, "Source", "HostPlcModel");
        }

        if (ContainsAny(parentTypeName, "PlcBlockUserGroup", "BlockGroup"))
        {
            foreach (var nestedBlock in EnumerateCollectionProperty(parent, "Blocks", issues, parentPath))
            {
                var blockName = TryReadString(nestedBlock, "Name") ?? nestedBlock.GetType().Name;
                var blockPath = $"{parentPath}/Blocks/{blockName}";
                yield return new PlcTraversalCandidate(nestedBlock, blockPath, ClassifyPlcObjectType(nestedBlock, blockPath), "HostPlcModel");
            }

            foreach (var nestedGroup in EnumerateCollectionProperty(parent, "Groups", issues, parentPath))
            {
                var groupName = TryReadString(nestedGroup, "Name") ?? nestedGroup.GetType().Name;
                var groupPath = $"{parentPath}/Groups/{groupName}";
                yield return new PlcTraversalCandidate(nestedGroup, groupPath, "BlockGroup", "HostPlcModel");
            }
        }
    }

    private static object? TryReadObjectProperty(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead || property.GetIndexParameters().Length > 0)
        {
            return null;
        }

        try
        {
            var value = property.GetValue(source);
            if (value is null || IsSimpleValue(value.GetType()))
            {
                return null;
            }

            return value;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<object> EnumerateCollectionProperty(object source, string propertyName, ICollection<HostIssue> issues, string sourcePath)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanRead || property.GetIndexParameters().Length > 0)
        {
            yield break;
        }

        object? value;
        try
        {
            value = property.GetValue(source);
        }
        catch (Exception exception)
        {
            issues.Add(new HostIssue
            {
                Scope = "OpennessTraversal",
                Message = "Collection property access failed during PLC model traversal.",
                Details = $"Node: {sourcePath}; Property: {propertyName}; {DescribeException(exception)}"
            });
            yield break;
        }

        if (value is not IEnumerable enumerable || value is string)
        {
            yield break;
        }

        var count = 0;
        foreach (var item in enumerable)
        {
            count++;
            if (count > MaxItemsPerEnumerableProperty)
            {
                yield break;
            }

            if (item is null || IsSimpleValue(item.GetType()))
            {
                continue;
            }

            yield return item;
        }
    }

    private static IEnumerable<(object Node, string Path)> ResolvePlcEntryPoints(object deviceRoot, string devicePath, ICollection<HostIssue> issues)
    {
        foreach (var serviceEntry in ResolvePlcEntryPointsFromServices(deviceRoot, devicePath, issues))
        {
            yield return serviceEntry;
        }

        foreach (var property in deviceRoot.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyTypeName = property.PropertyType.FullName ?? property.PropertyType.Name;
            var candidate = $"{property.Name} {propertyTypeName}";

            if (!ContainsAny(candidate, "Software", "Plc", "Program"))
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(deviceRoot);
            }
            catch (Exception exception)
            {
                issues.Add(new HostIssue
                {
                    Scope = "OpennessTraversal",
                    Message = "Skipping PLC entry property because value retrieval failed.",
                    Details = $"Node: {devicePath}; Property: {property.Name}; {DescribeException(exception)}"
                });
                continue;
            }

            if (value is null)
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

                    yield return (item, $"{devicePath}/{property.Name}");
                }

                continue;
            }

            if (IsSimpleValue(value.GetType()))
            {
                continue;
            }

            yield return (value, $"{devicePath}/{property.Name}");
        }
    }

    private static IEnumerable<(object Node, string Path)> ResolvePlcEntryPointsFromServices(object deviceRoot, string devicePath, ICollection<HostIssue> issues)
    {
        var serviceTypeCandidates = ResolveSoftwareContainerServiceTypes();

        if (serviceTypeCandidates.Length == 0)
        {
            yield break;
        }

        var visitedNodes = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var probeQueue = new Queue<(object Node, string Path, int Depth)>();
        probeQueue.Enqueue((deviceRoot, devicePath, 0));

        var probeCount = 0;

        while (probeQueue.Count > 0 && probeCount < 150)
        {
            var current = probeQueue.Dequeue();

            if (!visitedNodes.Add(current.Node))
            {
                continue;
            }

            probeCount++;
            _heartbeatSession?.UpdatePhase("TraversePlcServiceProbe", current.Path);

            foreach (var serviceType in serviceTypeCandidates)
            {
                if (!TryGetService(current.Node, serviceType, out var serviceInstance, out var serviceError))
                {
                    if (!string.IsNullOrWhiteSpace(serviceError) && serviceError!.Contains("MissingMethodException", StringComparison.OrdinalIgnoreCase))
                    {
                        issues.Add(new HostIssue
                        {
                            Scope = "OpennessTraversal",
                            Message = "Service probing failed while resolving PLC software entry points.",
                            Details = $"Node: {current.Path}; Service: {serviceType.FullName}; {serviceError}"
                        });
                    }

                    continue;
                }

                if (serviceInstance is null)
                {
                    continue;
                }

                var softwareProperty = serviceInstance.GetType().GetProperty("Software", BindingFlags.Public | BindingFlags.Instance);
                var software = softwareProperty?.GetValue(serviceInstance);

                if (software is null || IsSimpleValue(software.GetType()))
                {
                    continue;
                }

                yield return (software, $"{current.Path}/Services/{serviceType.Name}/Software");
            }

            if (current.Depth >= 2)
            {
                continue;
            }

            foreach (var child in EnumerateChildObjects(current.Node, current.Path, issues))
            {
                probeQueue.Enqueue((child, $"{current.Path}/{child.GetType().Name}", current.Depth + 1));
            }
        }
    }

    private static Type[] ResolveSoftwareContainerServiceTypes()
    {
        var candidates = new[]
        {
            "Siemens.Engineering.HW.Features.SoftwareContainer",
            "Siemens.Engineering.HW.Features.SoftwareContainerComposition"
        };

        var results = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var fullName in candidates)
            {
                var type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
                if (type is null)
                {
                    continue;
                }

                if (!results.Contains(type))
                {
                    results.Add(type);
                }
            }
        }

        return results.ToArray();
    }

    private static bool TryGetService(object node, Type serviceType, out object? serviceInstance, out string? error)
    {
        serviceInstance = null;
        error = null;

        var nodeType = node.GetType();

        try
        {
            var genericMethod = nodeType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method =>
                    method.Name == "GetService"
                    && method.IsGenericMethodDefinition
                    && method.GetGenericArguments().Length == 1
                    && method.GetParameters().Length == 0);

            if (genericMethod is not null)
            {
                var closed = genericMethod.MakeGenericMethod(serviceType);
                serviceInstance = closed.Invoke(node, Array.Empty<object>());

                if (serviceInstance is not null)
                {
                    return true;
                }
            }

            var typedMethod = nodeType.GetMethod("GetService", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Type) }, null);

            if (typedMethod is not null)
            {
                serviceInstance = typedMethod.Invoke(node, new object[] { serviceType });
                return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            error = DescribeException(exception);
            return false;
        }
    }

    private static void TryLoginToSafetyOfflineProgram(object projectRoot, ICollection<HostIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(_safetyOfflineProgramPassword))
        {
            return;
        }

        var safetyServiceTypes = ResolveSafetyAdministrationServiceTypes();

        if (safetyServiceTypes.Length == 0)
        {
            issues.Add(new HostIssue
            {
                Scope = "SafetyAdministration",
                Message = "Safety login requested but SafetyAdministration service type was not resolved.",
                Details = $"Resolved runtime assemblies did not expose known safety service types. Candidates: {string.Join(", ", SafetyAdministrationTypeCandidates)}"
            });
            return;
        }

        var queue = new Queue<(object Node, string Path, int Depth)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        queue.Enqueue((projectRoot, "Project", 0));

        var scannedNodes = 0;
        var successfulLogins = 0;
        var failedLogins = 0;
        var enqueuedNodes = 1;
        var droppedChildrenByQueueLimit = 0;
        var suppressedFailureIssues = 0;
        var failureIssuesLogged = 0;

        while (queue.Count > 0 && scannedNodes < MaxSafetyLoginProbeNodes)
        {
            var current = queue.Dequeue();

            if (current.Depth > MaxSafetyLoginDepth || !visited.Add(current.Node))
            {
                continue;
            }

            scannedNodes++;

            foreach (var serviceType in safetyServiceTypes)
            {
                if (!TryGetService(current.Node, serviceType, out var service, out _)
                    || service is null)
                {
                    continue;
                }

                if (TryInvokeSafetyLogin(service, out var error))
                {
                    successfulLogins++;
                }
                else
                {
                    failedLogins++;
                    if (failureIssuesLogged < MaxSafetyLoginFailureIssues)
                    {
                        issues.Add(new HostIssue
                        {
                            Scope = "SafetyAdministration",
                            Message = "Safety login attempt failed.",
                            Details = $"Node: {current.Path}; Service: {serviceType.FullName}; {error}"
                        });

                        failureIssuesLogged++;
                    }
                    else
                    {
                        suppressedFailureIssues++;
                    }
                }
            }

            var childrenAddedForNode = 0;
            foreach (var child in EnumerateChildObjects(current.Node, current.Path, issues))
            {
                if (childrenAddedForNode >= MaxSafetyLoginChildrenPerNode)
                {
                    break;
                }

                if (queue.Count >= MaxSafetyLoginQueueSize)
                {
                    droppedChildrenByQueueLimit++;
                    continue;
                }

                queue.Enqueue((child, $"{current.Path}/{child.GetType().Name}", current.Depth + 1));
                enqueuedNodes++;
                childrenAddedForNode++;
            }
        }

        if (suppressedFailureIssues > 0)
        {
            issues.Add(new HostIssue
            {
                Scope = "SafetyAdministration",
                Message = "Safety login failure diagnostics were truncated.",
                Details = $"Logged: {failureIssuesLogged}; Suppressed: {suppressedFailureIssues}; Limit: {MaxSafetyLoginFailureIssues}."
            });
        }

        issues.Add(new HostIssue
        {
            Scope = "SafetyAdministration",
            Message = successfulLogins > 0
                ? "Safety offline program login completed before traversal."
                : "Safety offline program login did not authenticate any accessible safety context.",
            Details = $"Successful logins: {successfulLogins}; Failed logins: {failedLogins}; Scanned nodes: {scannedNodes}; Enqueued nodes: {enqueuedNodes}; Queue drops: {droppedChildrenByQueueLimit}; Limits: depth={MaxSafetyLoginDepth}, nodes={MaxSafetyLoginProbeNodes}, queue={MaxSafetyLoginQueueSize}, children/node={MaxSafetyLoginChildrenPerNode}; Service types: {string.Join(", ", safetyServiceTypes.Select(type => type.FullName))}."
        });
    }

    private static Type[] ResolveSafetyAdministrationServiceTypes()
    {
        var results = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var typeName in SafetyAdministrationTypeCandidates)
            {
                var candidate = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                if (candidate is not null && !results.Contains(candidate))
                {
                    results.Add(candidate);
                }
            }

            var derivedCandidates = GetLoadableTypes(assembly)
                .Where(type =>
                    !string.IsNullOrWhiteSpace(type.FullName)
                    && type.FullName.Contains("Siemens.Engineering.Safety", StringComparison.Ordinal)
                    && type.Name.Contains("SafetyAdministration", StringComparison.OrdinalIgnoreCase));

            foreach (var candidate in derivedCandidates)
            {
                if (!results.Contains(candidate))
                {
                    results.Add(candidate);
                }
            }
        }

        return results.ToArray();
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    private static bool TryInvokeSafetyLogin(object serviceInstance, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(_safetyOfflineProgramPassword))
        {
            error = "Safety password is empty.";
            return false;
        }

        var method = serviceInstance.GetType().GetMethod(
            "LoginToSafetyOfflineProgram",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(SecureString) },
            null);

        if (method is null)
        {
            error = "Service does not expose LoginToSafetyOfflineProgram(SecureString).";
            return false;
        }

        var password = _safetyOfflineProgramPassword!;
        using var securePassword = CreateSecureString(password);

        try
        {
            method.Invoke(serviceInstance, new object[] { securePassword });
            return true;
        }
        catch (Exception exception)
        {
            var details = DescribeException(exception);
            if (details.Contains("already", StringComparison.OrdinalIgnoreCase)
                && details.Contains("login", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error = details;
            return false;
        }
    }

    private static SecureString CreateSecureString(string password)
    {
        var secure = new SecureString();

        foreach (var character in password)
        {
            secure.AppendChar(character);
        }

        secure.MakeReadOnly();
        return secure;
    }

    private static IEnumerable<object> EnumeratePlcChildObjects(object parent, string parentPath, ICollection<HostIssue> issues)
    {
        var properties = parent.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!IsPlcCandidateProperty(property) || ShouldSkipProperty(property))
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(parent);
            }
            catch (Exception exception)
            {
                issues.Add(new HostIssue
                {
                    Scope = "OpennessTraversal",
                    Message = "Skipping PLC property because value retrieval failed.",
                    Details = $"Node: {parentPath}; Property: {property.Name}; {DescribeException(exception)}"
                });
                continue;
            }

            if (value is null || IsSimpleValue(value.GetType()))
            {
                continue;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var itemCount = 0;
                foreach (var item in enumerable)
                {
                    itemCount++;
                    if (itemCount > MaxItemsPerEnumerableProperty)
                    {
                        break;
                    }

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

    private static IEnumerable<object> EnumerateChildObjects(object parent, string parentPath, ICollection<HostIssue> issues)
    {
        var properties = parent.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!IsCandidateChildProperty(property) || ShouldSkipProperty(property))
            {
                continue;
            }

            object? value;

            try
            {
                _heartbeatSession?.UpdatePhase("TraverseProperty", $"{parentPath}.{property.Name}");
                var propertyTimer = Stopwatch.StartNew();
                value = property.GetValue(parent);

                if (propertyTimer.Elapsed > SlowPropertyThreshold)
                {
                    issues.Add(new HostIssue
                    {
                        Scope = "OpennessTraversal",
                        Message = "Slow property access detected during traversal.",
                        Details = $"Node: {parentPath}; Property: {property.Name}; Elapsed: {propertyTimer.Elapsed:c}"
                    });
                }
            }
            catch (Exception exception)
            {
                issues.Add(new HostIssue
                {
                    Scope = "OpennessTraversal",
                    Message = "Skipping property because value retrieval failed.",
                    Details = $"Node: {parentPath}; Property: {property.Name}; {DescribeException(exception)}"
                });
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
                var itemCount = 0;

                IEnumerator? enumerator;
                try
                {
                    enumerator = enumerable.GetEnumerator();
                }
                catch (Exception exception)
                {
                    issues.Add(new HostIssue
                    {
                        Scope = "OpennessTraversal",
                        Message = "Skipping enumerable property because enumerator creation failed.",
                        Details = $"Node: {parentPath}; Property: {property.Name}; {DescribeException(exception)}"
                    });
                    continue;
                }

                if (enumerator is null)
                {
                    continue;
                }

                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = enumerator.MoveNext();
                    }
                    catch (Exception exception)
                    {
                        issues.Add(new HostIssue
                        {
                            Scope = "OpennessTraversal",
                            Message = "Skipping enumerable property because iteration failed.",
                            Details = $"Node: {parentPath}; Property: {property.Name}; {DescribeException(exception)}"
                        });
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    itemCount++;
                    if (itemCount > MaxItemsPerEnumerableProperty)
                    {
                        issues.Add(new HostIssue
                        {
                            Scope = "OpennessTraversal",
                            Message = "Enumerable item limit reached; remaining items were skipped.",
                            Details = $"Node: {parentPath}; Property: {property.Name}; Limit: {MaxItemsPerEnumerableProperty}"
                        });
                        break;
                    }

                    var item = enumerator.Current;

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

    private static bool IsCandidateChildProperty(PropertyInfo property)
    {
        var propertyName = property.Name;
        var typeName = property.PropertyType.FullName ?? property.PropertyType.Name;
        var candidate = $"{propertyName} {typeName}";

        if (ContainsAny(candidate,
                "Device", "Group", "Folder", "Collection", "Items", "Blocks", "BlockGroup", "Tags", "TagTable", "Types", "DataType",
                "Software", "Plc", "Screen", "Faceplate", "Template", "Recipe", "Alarm", "Connection", "Subnet", "Network", "Interface",
                "Port", "Module", "Library", "MasterCopies", "Users", "Audit", "Technology", "Motion", "Pid", "Safety", "Diagnostics"))
        {
            return true;
        }

        return typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && typeName.Contains("Siemens.Engineering", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlcCandidateProperty(PropertyInfo property)
    {
        var typeName = property.PropertyType.FullName ?? property.PropertyType.Name;
        var candidate = $"{property.Name} {typeName}";

        return ContainsAny(candidate,
            "Software", "Plc", "Block", "BlockGroup", "Group", "Tag", "TagTable", "DataType", "UDT", "Type", "UserType",
            "Source", "ExternalSource", "Technology", "Motion", "Pid", "Safety", "CompileUnit", "Network");
    }

    private static bool ShouldSkipProperty(PropertyInfo property)
    {
        var candidate = $"{property.Name} {property.PropertyType.FullName}";

        if (ContainsAny(candidate, "Image", "Bitmap", "Thumbnail", "Preview", "Binary", "Content", "Stream", "Byte[]", "Icon"))
        {
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> BuildNodeMetadata(object runtimeNode, string extractionStrategy, string qualifiedPath, ICollection<HostIssue> issues)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RuntimeType"] = runtimeNode.GetType().FullName ?? runtimeNode.GetType().Name,
            ["ExtractionStrategy"] = extractionStrategy,
            ["QualifiedPath"] = qualifiedPath
        };

        var scalarCount = 0;

        foreach (var property in runtimeNode.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (scalarCount >= MaxScalarMetadataEntries)
            {
                metadata["ScalarPropertyLimitReached"] = bool.TrueString;
                break;
            }

            if (!IsSafeScalarProperty(property))
            {
                continue;
            }

            object? value;
            Stopwatch? timer = null;

            try
            {
                timer = Stopwatch.StartNew();
                value = property.GetValue(runtimeNode);
            }
            catch
            {
                continue;
            }

            if (timer is not null && timer.Elapsed > SlowPropertyThreshold)
            {
                issues.Add(new HostIssue
                {
                    Scope = "OpennessTraversal",
                    Message = "Slow scalar property access detected during metadata extraction.",
                    Details = $"Node: {qualifiedPath}; Property: {property.Name}; Elapsed: {timer.Elapsed:c}"
                });
            }

            if (!TryConvertScalarToString(value, out var serializedValue))
            {
                continue;
            }

            if (serializedValue.Length > MaxScalarMetadataValueLength)
            {
                serializedValue = serializedValue.Substring(0, MaxScalarMetadataValueLength);
            }

            metadata[$"Prop.{property.Name}"] = serializedValue;
            scalarCount++;
        }

        metadata["ScalarPropertyCount"] = scalarCount.ToString();
        return metadata;
    }

    private static bool IsSafeScalarProperty(PropertyInfo property)
    {
        if (!property.CanRead || property.GetIndexParameters().Length > 0)
        {
            return false;
        }

        if (ShouldSkipProperty(property) || IsCandidateChildProperty(property) || IsPlcCandidateProperty(property))
        {
            return false;
        }

        var propertyType = property.PropertyType;

        if (typeof(IEnumerable).IsAssignableFrom(propertyType) && propertyType != typeof(string))
        {
            return false;
        }

        return IsSimpleValue(propertyType)
            || (Nullable.GetUnderlyingType(propertyType) is Type underlying && IsSimpleValue(underlying));
    }

    private static void EnrichWithDeepContent(
        object runtimeNode,
        string objectType,
        string qualifiedPath,
        Dictionary<string, string> metadata,
        ICollection<HostIssue> issues)
    {
        if (!ShouldExtractDeepContent(objectType, qualifiedPath))
        {
            return;
        }

        if (TryExportNodeToXml(runtimeNode, out var exportedXml, out var exportError))
        {
            var xmlWasTruncated = false;
            var xmlForMetadata = TruncateContent(exportedXml, MaxExportXmlChars, out xmlWasTruncated);
            metadata["Content.ExportXml"] = xmlForMetadata;
            metadata["Content.ExportXmlLength"] = exportedXml.Length.ToString();
            if (xmlWasTruncated)
            {
                metadata["Content.ExportXmlTruncated"] = bool.TrueString;
            }
        }
        else if (!string.IsNullOrWhiteSpace(exportError))
        {
            var exportErrorText = exportError!;
            string? safetyLoginDiagnostics = null;

            if (IsSafetyPermissionIssue(exportErrorText)
                && !string.IsNullOrWhiteSpace(_safetyOfflineProgramPassword)
                && TryLoginToSafetyContext(runtimeNode, qualifiedPath, issues, out safetyLoginDiagnostics)
                && TryExportNodeToXml(runtimeNode, out var retryExportXml, out _))
            {
                var xmlWasTruncated = false;
                var xmlForMetadata = TruncateContent(retryExportXml, MaxExportXmlChars, out xmlWasTruncated);
                metadata["Content.ExportXml"] = xmlForMetadata;
                metadata["Content.ExportXmlLength"] = retryExportXml.Length.ToString();
                metadata["SafetyLoginRetrySucceeded"] = bool.TrueString;
                if (!string.IsNullOrWhiteSpace(safetyLoginDiagnostics))
                {
                    metadata["SafetyLoginDiagnostics"] = safetyLoginDiagnostics!;
                }

                if (xmlWasTruncated)
                {
                    metadata["Content.ExportXmlTruncated"] = bool.TrueString;
                }

                return;
            }

            issues.Add(new HostIssue
            {
                Scope = "OpennessTraversal",
                Message = "Deep export XML extraction failed for runtime node.",
                Details = $"Node: {qualifiedPath}; ObjectType: {objectType}; {exportErrorText}"
            });

            if (IsSafetyPermissionIssue(exportErrorText))
            {
                issues.Add(new HostIssue
                {
                    Scope = "SafetyAdministration",
                    Message = "Safety-protected block export is not permitted even after optional login handling.",
                    Details = string.IsNullOrWhiteSpace(safetyLoginDiagnostics)
                        ? $"Node: {qualifiedPath}; ObjectType: {objectType}; Retry info: {exportErrorText}"
                        : $"Node: {qualifiedPath}; ObjectType: {objectType}; Retry info: {exportErrorText}; Login diagnostics: {safetyLoginDiagnostics}"
                });
            }
        }

        if (TryExtractSourceText(runtimeNode, out var sourceText)
            || TryExtractSourceViaMethods(runtimeNode, out sourceText))
        {
            var sourceWasTruncated = false;
            var sourceForMetadata = TruncateContent(sourceText, MaxSourceTextChars, out sourceWasTruncated);
            metadata["Content.SourceText"] = sourceForMetadata;
            metadata["Content.SourceTextLength"] = sourceText.Length.ToString();
            if (sourceWasTruncated)
            {
                metadata["Content.SourceTextTruncated"] = bool.TrueString;
            }

            return;
        }

        if (metadata.TryGetValue("Content.ExportXml", out var xml)
            && !string.IsNullOrWhiteSpace(xml)
            && TryExtractSourceTextFromExportXml(xml, out var extractedFromXml))
        {
            var sourceWasTruncated = false;
            var sourceForMetadata = TruncateContent(extractedFromXml, MaxSourceTextChars, out sourceWasTruncated);
            metadata["Content.SourceText"] = sourceForMetadata;
            metadata["Content.SourceTextLength"] = extractedFromXml.Length.ToString();
            metadata["Content.SourceOrigin"] = "ExportXml";
            if (sourceWasTruncated)
            {
                metadata["Content.SourceTextTruncated"] = bool.TrueString;
            }
        }
    }

    private static bool ShouldExtractDeepContent(string objectType, string qualifiedPath)
    {
        if (objectType is "OB" or "FB" or "FC" or "DB" or "InstanceDB" or "Tag" or "TagTable" or "UDT" or "TechnologyObject" or "Source")
        {
            return true;
        }

        return qualifiedPath.Contains("Software", StringComparison.OrdinalIgnoreCase)
            || qualifiedPath.Contains("Blocks", StringComparison.OrdinalIgnoreCase)
            || qualifiedPath.Contains("Tag", StringComparison.OrdinalIgnoreCase)
            || qualifiedPath.Contains("DataType", StringComparison.OrdinalIgnoreCase)
            || qualifiedPath.Contains("Technology", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExportNodeToXml(object runtimeNode, out string xmlContent, out string? error)
    {
        xmlContent = string.Empty;
        error = null;

        var exportMethods = runtimeNode.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => string.Equals(method.Name, "Export", StringComparison.OrdinalIgnoreCase))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length < 1 || parameters.Length > 2)
                {
                    return false;
                }

                return parameters[0].ParameterType == typeof(FileInfo)
                    || parameters[0].ParameterType == typeof(string);
            })
            .OrderByDescending(method => method.GetParameters()[0].ParameterType == typeof(FileInfo))
            .ThenByDescending(method => method.GetParameters().Length == 2)
            .ToArray();

        if (exportMethods.Length == 0)
        {
            return false;
        }

        var errors = new List<string>();

        foreach (var exportMethod in exportMethods)
        {
            if (TryExportWithMethod(runtimeNode, exportMethod, out xmlContent, out var methodError))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(methodError))
            {
                errors.Add(methodError!);
            }
        }

        if (errors.Count > 0)
        {
            error = string.Join(" | ", errors.Distinct(StringComparer.Ordinal));
        }

        return false;
    }

    private static bool TryExportWithMethod(object runtimeNode, MethodInfo exportMethod, out string xmlContent, out string? error)
    {
        xmlContent = string.Empty;
        error = null;

        var extension = ".xml";
        var tempPath = Path.Combine(Path.GetTempPath(), $"tia-exporter-{Guid.NewGuid():N}{extension}");

        try
        {
            var parameters = exportMethod.GetParameters();
            var firstParameterType = parameters[0].ParameterType;

            object argument = firstParameterType == typeof(FileInfo)
                ? (object)new FileInfo(tempPath)
                : tempPath;

            object[] arguments;
            if (parameters.Length == 1)
            {
                arguments = new object[] { argument };
            }
            else
            {
                var optionsArgument = ResolveExportOptionsArgument(parameters[1].ParameterType);
                if (optionsArgument is null)
                {
                    error = $"Method {exportMethod.Name} has unsupported options type '{parameters[1].ParameterType.FullName}'.";
                    return false;
                }

                arguments = new[] { argument, optionsArgument };
            }

            _ = exportMethod.Invoke(runtimeNode, arguments);

            if (!File.Exists(tempPath))
            {
                error = $"Method {exportMethod} executed but produced no output file.";
                return false;
            }

            var fileInfo = new FileInfo(tempPath);

            if (fileInfo.Length > MaxExportXmlFileBytes)
            {
                error = $"Method {exportMethod} produced XML ({fileInfo.Length} bytes) exceeding limit ({MaxExportXmlFileBytes} bytes).";
                return false;
            }

            xmlContent = File.ReadAllText(tempPath);
            if (string.IsNullOrWhiteSpace(xmlContent))
            {
                error = $"Method {exportMethod} produced an empty XML file.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = $"Method {exportMethod} failed: {DescribeException(exception)}";
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static object? ResolveExportOptionsArgument(Type optionsType)
    {
        try
        {
            if (optionsType.IsEnum)
            {
                if (Enum.GetNames(optionsType).Any(name => string.Equals(name, "WithDefaults", StringComparison.OrdinalIgnoreCase)))
                {
                    return Enum.Parse(optionsType, "WithDefaults", ignoreCase: true);
                }

                return Activator.CreateInstance(optionsType);
            }

            var defaultsField = optionsType.GetField("WithDefaults", BindingFlags.Public | BindingFlags.Static);
            if (defaultsField is not null)
            {
                return defaultsField.GetValue(null);
            }

            var defaultsProperty = optionsType.GetProperty("WithDefaults", BindingFlags.Public | BindingFlags.Static);
            if (defaultsProperty is not null)
            {
                return defaultsProperty.GetValue(null);
            }

            return Activator.CreateInstance(optionsType);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryExtractSourceText(object runtimeNode, out string sourceText)
    {
        sourceText = string.Empty;

        var candidates = new[]
        {
            "Source",
            "Text",
            "Code",
            "StatementList",
            "SclSource",
            "ExternalSource"
        };

        foreach (var propertyName in candidates)
        {
            var property = runtimeNode.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                var value = property.GetValue(runtimeNode);
                if (value is null)
                {
                    continue;
                }

                var text = value.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                sourceText = text;
                return true;
            }
            catch
            {
                // Ignore and continue probing.
            }
        }

        return false;
    }

    private static bool TryExtractSourceViaMethods(object runtimeNode, out string sourceText)
    {
        sourceText = string.Empty;

        var methodCandidates = new[]
        {
            "GenerateSource",
            "GetSource",
            "GetText",
            "ToText",
            "CreateSource",
            "ExportToString"
        };

        var methods = runtimeNode.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.ReturnType == typeof(string) && method.GetParameters().Length == 0)
            .ToArray();

        foreach (var candidate in methodCandidates)
        {
            var method = methods.FirstOrDefault(item => string.Equals(item.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (method is null)
            {
                continue;
            }

            try
            {
                var value = method.Invoke(runtimeNode, Array.Empty<object>()) as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                sourceText = value!;
                return true;
            }
            catch
            {
                // Ignore and continue probing.
            }
        }

        return false;
    }

    private static bool IsSafetyPermissionIssue(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
            || error.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || error.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || error.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
            || error.Contains("nicht zul", StringComparison.OrdinalIgnoreCase)
            || error.Contains("zugriff verweigert", StringComparison.OrdinalIgnoreCase)
            || error.Contains("safety", StringComparison.OrdinalIgnoreCase)
            || error.Contains("f-program", StringComparison.OrdinalIgnoreCase)
            || error.Contains("offline program", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryLoginToSafetyContext(object runtimeNode, string qualifiedPath, ICollection<HostIssue> issues, out string? diagnostics)
    {
        diagnostics = null;
        var safetyServiceTypes = ResolveSafetyAdministrationServiceTypes();
        if (safetyServiceTypes.Length == 0)
        {
            diagnostics = "No safety service types resolved.";
            return false;
        }

        object? current = runtimeNode;
        var depth = 0;
        var attemptedServices = 0;
        var discoveredServices = 0;

        while (current is not null && depth < 6)
        {
            foreach (var serviceType in safetyServiceTypes)
            {
                if (!TryGetService(current, serviceType, out var service, out _)
                    || service is null)
                {
                    continue;
                }

                discoveredServices++;

                if (TryInvokeSafetyLogin(service, out var error))
                {
                    diagnostics = $"Safety login succeeded at depth {depth} using service {serviceType.FullName}. Discovered services: {discoveredServices}; attempted logins: {attemptedServices + 1}.";
                    return true;
                }

                attemptedServices++;

                if (!string.IsNullOrWhiteSpace(error))
                {
                    issues.Add(new HostIssue
                    {
                        Scope = "SafetyAdministration",
                        Message = "Safety login retry failed for protected export node.",
                        Details = $"Node: {qualifiedPath}; Service: {serviceType.FullName}; {error}"
                    });
                }
            }

            current = TryGetParentNode(current);
            depth++;
        }

        diagnostics = discoveredServices == 0
            ? "No safety service found on node context chain."
            : $"Safety services found ({discoveredServices}) but login failed for all attempted services ({attemptedServices}).";

        return false;
    }

    private static object? TryGetParentNode(object node)
    {
        var parentPropertyNames = new[] { "Parent", "Owner", "ParentObject", "ParentGroup", "ContainingObject" };

        foreach (var propertyName in parentPropertyNames)
        {
            var property = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            try
            {
                var value = property.GetValue(node);
                if (value is not null && !IsSimpleValue(value.GetType()))
                {
                    return value;
                }
            }
            catch
            {
                // Ignore and probe next known parent property.
            }
        }

        return null;
    }

    private static bool TryExtractSourceTextFromExportXml(string xml, out string sourceText)
    {
        sourceText = string.Empty;

        if (xml.Length > MaxXmlSourceParseChars)
        {
            return false;
        }

        try
        {
            var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);

            var preferredElementNames = new[]
            {
                "Source",
                "SourceText",
                "STSource",
                "SCLSource",
                "StatementList",
                "StructuredText",
                "Code",
                "NetworkSource",
                "Implementation"
            };

            foreach (var name in preferredElementNames)
            {
                var matches = document
                    .Descendants()
                    .Where(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    .Select(element => NormalizeSourceText(element.Value))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();

                if (matches.Length > 0)
                {
                    sourceText = string.Join(Environment.NewLine + Environment.NewLine, matches.Distinct(StringComparer.Ordinal));
                    return true;
                }
            }

            var fallbackTextNodes = document
                .Descendants()
                .Select(element => NormalizeSourceText(element.Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Where(value => value.Length >= 20)
                .Distinct(StringComparer.Ordinal)
                .Take(300)
                .ToArray();

            if (fallbackTextNodes.Length == 0)
            {
                return false;
            }

            sourceText = string.Join(Environment.NewLine, fallbackTextNodes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeSourceText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var lines = raw
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string TruncateContent(string content, int maxChars, out bool wasTruncated)
    {
        wasTruncated = false;

        if (content.Length <= maxChars)
        {
            return content;
        }

        wasTruncated = true;
        return content.Substring(0, maxChars);
    }

    private static bool TryConvertScalarToString(object? value, out string serialized)
    {
        if (value is null)
        {
            serialized = string.Empty;
            return true;
        }

        switch (value)
        {
            case DateTime dateTime:
                serialized = dateTime.ToString("O");
                return true;
            case DateTimeOffset dateTimeOffset:
                serialized = dateTimeOffset.ToString("O");
                return true;
            case TimeSpan timeSpan:
                serialized = timeSpan.ToString("c");
                return true;
            default:
                serialized = value.ToString() ?? string.Empty;
                return true;
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

    private static string ClassifyObjectType(string runtimeTypeName, string qualifiedPath)
    {
        var candidate = $"{runtimeTypeName} {qualifiedPath}";

        if (ContainsAny(candidate, "FB", "FunctionBlock"))
        {
            return "FunctionBlock";
        }

        if (ContainsAny(candidate, "FC", "Function"))
        {
            return "Function";
        }

        if (ContainsAny(candidate, "OB", "OrganizationBlock"))
        {
            return "OrganizationBlock";
        }

        if (ContainsAny(candidate, "DB", "DataBlock"))
        {
            return "DataBlock";
        }

        if (ContainsAny(candidate, "Tag", "TagTable", "PlcTag"))
        {
            return "Tag";
        }

        if (ContainsAny(candidate, "UDT", "DataType"))
        {
            return "DataType";
        }

        if (ContainsAny(candidate, "Hmi", "Screen", "Faceplate", "Recipe", "Alarm"))
        {
            return "HmiObject";
        }

        if (ContainsAny(candidate, "Network", "Profinet", "Profibus", "Connection"))
        {
            return "NetworkObject";
        }

        if (ContainsAny(candidate, "Library"))
        {
            return "LibraryObject";
        }

        return "UnmappedRuntimeNode";
    }

    private static string ClassifyPlcObjectType(object runtimeNode, string qualifiedPath)
    {
        var runtimeTypeFullName = runtimeNode.GetType().FullName ?? runtimeNode.GetType().Name;
        var runtimeTypeName = runtimeNode.GetType().Name;
        var nodeName = TryReadString(runtimeNode, "Name") ?? TryReadString(runtimeNode, "DisplayName") ?? runtimeTypeName;
        var candidate = $"{runtimeTypeFullName} {runtimeTypeName} {nodeName} {qualifiedPath}";

        if (ContainsAny(candidate, ".SW.Blocks.OB", "OrganizationBlock") || Regex.IsMatch(nodeName, "^OB\\d+", RegexOptions.IgnoreCase))
        {
            return "OB";
        }

        if (ContainsAny(candidate, ".SW.Blocks.FB", "FunctionBlock") || Regex.IsMatch(nodeName, "^FB\\d+", RegexOptions.IgnoreCase))
        {
            return "FB";
        }

        if (ContainsAny(candidate, ".SW.Blocks.FC", "Function") || Regex.IsMatch(nodeName, "^FC\\d+", RegexOptions.IgnoreCase))
        {
            return "FC";
        }

        if (ContainsAny(candidate, "InstanceDataBlock", "InstanceDB", "InstanceDb") || Regex.IsMatch(nodeName, "^IDB\\d+", RegexOptions.IgnoreCase))
        {
            return "InstanceDB";
        }

        if (ContainsAny(candidate, ".SW.Blocks.DB", "DataBlock") || Regex.IsMatch(nodeName, "^DB\\d+", RegexOptions.IgnoreCase))
        {
            return "DB";
        }

        if (ContainsAny(candidate, "TagTable"))
        {
            return "TagTable";
        }

        if (ContainsAny(candidate, "PlcTag", "Tag"))
        {
            return "Tag";
        }

        if (ContainsAny(candidate, "UserDataType", "UDT", "DataType"))
        {
            return "UDT";
        }

        if (ContainsAny(candidate, "Technology", "Motion", "Pid", "Safety"))
        {
            return "TechnologyObject";
        }

        if (ContainsAny(candidate, "Source", "ExternalSource"))
        {
            return "Source";
        }

        return ClassifyObjectType(runtimeTypeName, qualifiedPath);
    }

    private static bool ContainsRuntimePathData(string path, string objectType)
    {
        if (objectType is "OB" or "FB" or "FC" or "DB" or "InstanceDB" or "Tag" or "TagTable" or "UDT" or "TechnologyObject" or "Source")
        {
            return true;
        }

        return path.Contains("/BlockGroup", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/Blocks/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/TagTableGroup", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/TagTables/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/TypeGroup", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/Types/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/TechnologyObjects/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/ExternalSources/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/Sources/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyRecursiveHardwarePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Contains("/DeviceItemImpl/DeviceItemImpl/DeviceItemImpl", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/HwIdentifier/HwIdentifier/HwIdentifier", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/Address/Address/Address", StringComparison.OrdinalIgnoreCase)
            || HasRepeatedPathSegment(path, "DeviceItemImpl", 3)
            || HasRepeatedPathSegment(path, "HwIdentifier", 3)
            || HasRepeatedPathSegment(path, "Address", 4);
    }

    private static bool HasRepeatedPathSegment(string path, string segment, int minRun)
    {
        var parts = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var run = 0;
        foreach (var part in parts)
        {
            if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
            {
                run++;
                if (run >= minRun)
                {
                    return true;
                }

                continue;
            }

            run = 0;
        }

        return false;
    }

    private static bool ContainsAny(string candidate, params string[] terms) =>
        terms.Any(term => candidate.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

    private static string? TryReadString(object node, string propertyName)
    {
        var property = node.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(node)?.ToString();
    }

    private static void TryCloseProject(object? project)
    {
        if (project is null)
        {
            return;
        }

        var closeMethod = project.GetType().GetMethod("Close", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
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
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(installPath, "Siemens.Engineering.dll"),
            Path.Combine(installPath, "Bin", "Siemens.Engineering.dll"),
            Path.Combine(installPath, "PublicAPI", "Siemens.Engineering.dll"),
            Path.Combine(installPath, "PublicAPI", "V20", "Siemens.Engineering.dll"),
            Path.Combine(installPath, "PublicAPI", "V19", "Siemens.Engineering.dll"),
            Path.Combine(installPath, "PublicAPI", "V18", "Siemens.Engineering.dll")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryInitializeSiemensResolver(ICollection<string>? details = null)
    {
        try
        {
            var apiType = ResolveOpennessApiType();

            if (apiType is null)
            {
                details?.Add("Siemens Openness resolver API type was not found; continuing with manual assembly loading.");
                return;
            }

            var globalMethod = apiType.GetMethod("Global", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            var globalInstance = globalMethod?.Invoke(null, Array.Empty<object>());

            if (globalInstance is null)
            {
                details?.Add("Siemens Openness resolver Global() returned null; continuing with manual assembly loading.");
                return;
            }

            var opennessMethod = globalInstance.GetType().GetMethod("Openness", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            var opennessInstance = opennessMethod?.Invoke(globalInstance, Array.Empty<object>());

            if (opennessInstance is null)
            {
                details?.Add("Siemens Openness resolver Openness() returned null; continuing with manual assembly loading.");
                return;
            }

            var initializeMethod = opennessInstance.GetType().GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

            if (initializeMethod is null)
            {
                details?.Add("Siemens Openness resolver Initialize() method was not found; continuing with manual assembly loading.");
                return;
            }

            initializeMethod.Invoke(opennessInstance, Array.Empty<object>());
            details?.Add("Siemens Openness resolver initialized successfully.");
        }
        catch (Exception exception)
        {
            details?.Add($"Siemens Openness resolver initialization failed; using manual assembly loading. {DescribeException(exception)}");
        }
    }

    private static Type? ResolveOpennessApiType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("Siemens.Collaboration.Net.TiaPortal.Openness.Api", throwOnError: false, ignoreCase: false);
            if (type is not null)
            {
                return type;
            }
        }

        var candidateAssemblyNames = new[]
        {
            "Siemens.Collaboration.Net.TiaPortal.Openness.Extensions",
            "Siemens.Collaboration.Net.TiaPortal.Openness.Resolver"
        };

        foreach (var assemblyName in candidateAssemblyNames)
        {
            try
            {
                var loadedAssembly = Assembly.Load(assemblyName);
                var type = loadedAssembly.GetType("Siemens.Collaboration.Net.TiaPortal.Openness.Api", throwOnError: false, ignoreCase: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch
            {
                // Ignore and continue with manual fallback.
            }
        }

        return null;
    }

    private static string DescribeException(Exception exception)
    {
        var segments = new List<string>();
        var current = exception;

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

    private static void WriteJson(object response)
    {
        var serializer = new DataContractJsonSerializer(response.GetType());
        using var memoryStream = new MemoryStream();
        serializer.WriteObject(memoryStream, response);
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream, Encoding.UTF8);
        Console.Write(reader.ReadToEnd());
    }
}

internal sealed class HeartbeatSession : IDisposable
{
    private readonly Timer _timer;
    private string _phase = "Starting";
    private string _detail = "Initializing";

    public HeartbeatSession()
    {
        _timer = new Timer(OnTick, state: null, dueTime: TimeSpan.Zero, period: TimeSpan.FromSeconds(3));
    }

    public void UpdatePhase(string phase, string detail)
    {
        _phase = phase;
        _detail = detail;
        Emit("phase");
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private void OnTick(object? state) => Emit("alive");

    private void Emit(string state)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        Console.Error.WriteLine($"HB|{timestamp}|{state}|{_phase}|{_detail}");
    }
}

internal sealed class HostOptions
{
    public string ProjectPath { get; set; } = string.Empty;

    public string InstallPath { get; set; } = string.Empty;

    public bool IsPreview { get; set; }

    public IReadOnlyCollection<string> IncludedDomains { get; set; } = Array.Empty<string>();

    public string? SafetyOfflineProgramPassword { get; set; }

    public static HostOptions Parse(string[] args, bool requireProjectPath = true)
    {
        var projectPath = GetValue(args, "--project");
        var installPath = GetValue(args, "--install");
        var rawDomains = GetValue(args, "--domains");
        var safetyPassword = GetValue(args, "--safety-password")
            ?? Environment.GetEnvironmentVariable("TIA_EXPORTER_SAFETY_OFFLINE_PASSWORD");

        if (requireProjectPath && string.IsNullOrWhiteSpace(projectPath))
        {
            throw new ArgumentException("Missing --project argument.");
        }

        if (string.IsNullOrWhiteSpace(installPath))
        {
            throw new ArgumentException("Missing --install argument.");
        }

        return new HostOptions
        {
            ProjectPath = projectPath ?? string.Empty,
            InstallPath = installPath!,
            IsPreview = args.Any(argument => string.Equals(argument, "--preview", StringComparison.OrdinalIgnoreCase)),
            IncludedDomains = ParseDomains(rawDomains),
            SafetyOfflineProgramPassword = safetyPassword
        };
    }

    private static IReadOnlyCollection<string> ParseDomains(string? serializedDomains)
    {
        if (string.IsNullOrWhiteSpace(serializedDomains))
        {
            return Array.Empty<string>();
        }

        var domains = serializedDomains!;

        return domains
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(domain => domain.Trim())
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetValue(IReadOnlyList<string> args, string key)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}

internal sealed class PlcTraversalCandidate
{
    public PlcTraversalCandidate(object node, string? path, string? objectType, string? strategy)
    {
        Node = node;
        Path = path;
        ObjectType = objectType;
        Strategy = strategy;
    }

    public object Node { get; }

    public string? Path { get; }

    public string? ObjectType { get; }

    public string? Strategy { get; }
}

internal sealed class PreviewFallbackResult
{
    public PreviewFallbackResult(int blockCount, int blockGroupCount)
    {
        BlockCount = blockCount;
        BlockGroupCount = blockGroupCount;
    }

    public int BlockCount { get; }

    public int BlockGroupCount { get; }
}

internal sealed class PreviewScanDiagnostics
{
    public int PlcEntryPointsDiscovered { get; set; }

    public int BlockGroupsDiscovered { get; set; }

    public int ObBlocksDiscovered { get; set; }

    public int FbBlocksDiscovered { get; set; }

    public int FcBlocksDiscovered { get; set; }

    public int DbBlocksDiscovered { get; set; }

    public int BlockFallbackActivations { get; set; }

    public int BlockFallbackNodesVisited { get; set; }

    public int EntryLimitHits { get; set; }
}

internal sealed class TraversalDomainScope
{
    private readonly HashSet<string> _includedDomains;

    private TraversalDomainScope(HashSet<string> includedDomains)
    {
        _includedDomains = includedDomains;
    }

    public bool HasRestrictions => _includedDomains.Count > 0;

    public bool IncludePlcFocusedGraph => !HasRestrictions || _includedDomains.Overlaps(PlcFocusedDomains);

    public bool IncludeGenericSoftwareGraph => !HasRestrictions || _includedDomains.Overlaps(GenericSoftwareDomains);

    public bool AllowPreviewScan => !HasRestrictions || IncludePlcFocusedGraph || IncludeGenericSoftwareGraph;

    public static TraversalDomainScope FromRaw(IReadOnlyCollection<string>? rawDomains)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (rawDomains is null)
        {
            return new TraversalDomainScope(normalized);
        }

        foreach (var rawDomain in rawDomains)
        {
            if (string.IsNullOrWhiteSpace(rawDomain))
            {
                continue;
            }

            normalized.Add(rawDomain.Trim());
        }

        return new TraversalDomainScope(normalized);
    }

    public bool IsCandidateAllowed(string objectType, string qualifiedPath)
    {
        if (!HasRestrictions)
        {
            return true;
        }

        var domain = ResolveDomain(objectType, qualifiedPath);
        return _includedDomains.Contains(domain);
    }

    private static string ResolveDomain(string objectType, string qualifiedPath)
    {
        if (IsAny(objectType, "OB", "FB", "FC", "DB", "InstanceDB", "BlockGroup", "Source"))
        {
            return "Blocks";
        }

        if (IsAny(objectType, "Tag", "TagTable", "TagTableGroup"))
        {
            return "Tags";
        }

        if (IsAny(objectType, "UDT", "DataType", "Type", "TypeGroup"))
        {
            return "Udts";
        }

        if (objectType.IndexOf("Hmi", StringComparison.OrdinalIgnoreCase) >= 0 || objectType.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Hmi";
        }

        if (objectType.IndexOf("Technology", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Technology";
        }

        if (objectType.IndexOf("Library", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Libraries";
        }

        if (objectType.IndexOf("Diagnostic", StringComparison.OrdinalIgnoreCase) >= 0 || objectType.IndexOf("Audit", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Diagnostics";
        }

        if (IsAny(objectType, "Device", "Module", "Rack", "Cpu"))
        {
            return "Hardware";
        }

        if (objectType.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0 || qualifiedPath.IndexOf("/Network", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Network";
        }

        if (objectType.IndexOf("Project", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(qualifiedPath, "Project", StringComparison.OrdinalIgnoreCase))
        {
            return "Project";
        }

        if (objectType.IndexOf("Plc", StringComparison.OrdinalIgnoreCase) >= 0 || objectType.IndexOf("Software", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Plc";
        }

        if (qualifiedPath.IndexOf("/Blocks/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Blocks";
        }

        if (qualifiedPath.IndexOf("/Tag", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Tags";
        }

        if (qualifiedPath.IndexOf("/Type", StringComparison.OrdinalIgnoreCase) >= 0 || qualifiedPath.IndexOf("/UDT", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Udts";
        }

        if (qualifiedPath.IndexOf("/Technology", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Technology";
        }

        return "Metadata";
    }

    private static bool IsAny(string value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly HashSet<string> PlcFocusedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "Plc",
        "Blocks",
        "Tags",
        "Udts",
        "Technology"
    };

    private static readonly HashSet<string> GenericSoftwareDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hardware",
        "Network",
        "Hmi",
        "Libraries",
        "Diagnostics",
        "Metadata"
    };
}

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();

    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

    public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}

[DataContract]
internal sealed class HostTraversalResponse
{
    [DataMember(Name = "projectName")]
    public string? ProjectName { get; set; }

    [DataMember(Name = "projectPath")]
    public string ProjectPath { get; set; } = string.Empty;

    [DataMember(Name = "objects")]
    public List<HostObjectNode> Objects { get; set; } = new();

    [DataMember(Name = "issues")]
    public List<HostIssue> Issues { get; set; } = new();
}

[DataContract]
internal sealed class HostObjectNode
{
    [DataMember(Name = "objectType")]
    public string ObjectType { get; set; } = string.Empty;

    [DataMember(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [DataMember(Name = "qualifiedPath")]
    public string QualifiedPath { get; set; } = string.Empty;

    [DataMember(Name = "depth")]
    public int Depth { get; set; }

    [DataMember(Name = "metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

[DataContract]
internal sealed class HostIssue
{
    [DataMember(Name = "scope")]
    public string Scope { get; set; } = string.Empty;

    [DataMember(Name = "message")]
    public string Message { get; set; } = string.Empty;

    [DataMember(Name = "details")]
    public string? Details { get; set; }
}

[DataContract]
internal sealed class HostHealthResponse
{
    [DataMember(Name = "healthy")]
    public bool Healthy { get; set; }

    [DataMember(Name = "details")]
    public List<string> Details { get; set; } = new();
}
