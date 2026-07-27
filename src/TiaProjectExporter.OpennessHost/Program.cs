using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TiaProjectExporter.OpennessHost;

internal static class Program
{
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

        var assemblyPath = ResolveEngineeringAssemblyPath(options.InstallPath);

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
        }

        return BuildResult(options.ProjectPath, objects, issues);
    }

    private static HostTraversalResponse BuildResult(string projectPath, IReadOnlyList<HostObjectNode> objects, IReadOnlyList<HostIssue> issues)
    {
        return new HostTraversalResponse
        {
            ProjectName = Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath = projectPath,
            Objects = objects,
            Issues = issues
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
        var projectName = TryReadString(project, "Name");

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            objects.Add(new HostObjectNode
            {
                ObjectType = "ProjectMetadata",
                Name = projectName,
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
        var queue = new Queue<(object Node, string Path, int Depth)>();
        queue.Enqueue((root, rootPath, 1));

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var discoveredCount = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current.Depth > 6)
            {
                continue;
            }

            foreach (var child in EnumerateChildObjects(current.Node))
            {
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
                queue.Enqueue((child, childPath, current.Depth + 1));
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

    private static void WriteJson(HostTraversalResponse response)
    {
        var serializer = new DataContractJsonSerializer(typeof(HostTraversalResponse));
        using var memoryStream = new MemoryStream();
        serializer.WriteObject(memoryStream, response);
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream, Encoding.UTF8);
        Console.Write(reader.ReadToEnd());
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
            InstallPath = installPath
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
