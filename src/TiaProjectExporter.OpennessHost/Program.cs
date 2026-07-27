using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace TiaProjectExporter.OpennessHost;

internal static class Program
{
    private const int MaxTraversalDepth = 6;
    private const int MaxChildrenPerNode = 2000;
    private const int MaxItemsPerEnumerableProperty = 1000;
    private static readonly TimeSpan SlowPropertyThreshold = TimeSpan.FromSeconds(2);

    private static HeartbeatSession? _heartbeatSession;

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
                    ["SourcePath"] = options.ProjectPath
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
                    ["AssemblyName"] = engineeringAssembly.GetName().Name ?? "Unknown"
                }
            });

            _heartbeatSession.UpdatePhase("OpenProject", "Opening TIA project");
            project = OpenProject(tiaPortal, options.ProjectPath);

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
            TraverseProject(project, objects, issues);
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

    private static void TraverseProject(object project, ICollection<HostObjectNode> objects, ICollection<HostIssue> issues)
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
                Metadata = new Dictionary<string, string>
                {
                    ["RuntimeType"] = project.GetType().FullName ?? "Unknown"
                }
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
                Metadata = new Dictionary<string, string>
                {
                    ["RuntimeType"] = device.GetType().FullName ?? "Unknown"
                }
            });

            deviceCount++;
            TraverseSoftwareGraph(device, devicePath, objects, issues);
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

    private static void TraverseSoftwareGraph(object root, string rootPath, ICollection<HostObjectNode> objects, ICollection<HostIssue> issues)
    {
        _heartbeatSession?.UpdatePhase("TraverseSoftware", $"Root: {rootPath}");
        var queue = new Queue<(object Node, string Path, int Depth)>();
        queue.Enqueue((root, rootPath, 1));

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredCount = 0;

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
                    Metadata = new Dictionary<string, string>
                    {
                        ["RuntimeType"] = child.GetType().FullName ?? childTypeName,
                        ["ExtractionStrategy"] = "HostReflection"
                    }
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

    private static bool ShouldSkipProperty(PropertyInfo property)
    {
        var candidate = $"{property.Name} {property.PropertyType.FullName}";

        if (ContainsAny(candidate, "Image", "Bitmap", "Thumbnail", "Preview", "Binary", "Content", "Stream", "Byte[]", "Icon"))
        {
            return true;
        }

        return false;
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

    public static HostOptions Parse(string[] args, bool requireProjectPath = true)
    {
        var projectPath = GetValue(args, "--project");
        var installPath = GetValue(args, "--install");

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
            InstallPath = installPath!
        };
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
