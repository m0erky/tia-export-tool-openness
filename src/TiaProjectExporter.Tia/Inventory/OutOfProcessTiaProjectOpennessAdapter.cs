using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Executes Siemens Openness traversal in a dedicated external host process.
/// </summary>
public sealed class OutOfProcessTiaProjectOpennessAdapter : ITiaProjectOpennessAdapter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITiaInstallationDiscoveryService _installationDiscoveryService;
    private readonly ILogger<OutOfProcessTiaProjectOpennessAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutOfProcessTiaProjectOpennessAdapter"/> class.
    /// </summary>
    public OutOfProcessTiaProjectOpennessAdapter(
        ITiaInstallationDiscoveryService installationDiscoveryService,
        ILogger<OutOfProcessTiaProjectOpennessAdapter> logger)
    {
        _installationDiscoveryService = installationDiscoveryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TiaProjectTraversalResult> TraverseAsync(
        string projectPath,
        string? tiaInstallationPathOverride,
        TiaTraversalDetailLevel detailLevel,
        CancellationToken cancellationToken,
        IReadOnlyCollection<ExportDomain>? includedDomains = null,
        string? safetyOfflineProgramPassword = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var objects = new List<TiaProjectObjectNode>
        {
            new(
                ObjectType: "Project",
                Name: Path.GetFileNameWithoutExtension(projectPath),
                QualifiedPath: "Project",
                Depth: 0,
                Metadata: new Dictionary<string, string>
                {
                    ["SourcePath"] = projectPath,
                    ["TraversalMode"] = "OutOfProcess"
                })
        };

        if (!OperatingSystem.IsWindows())
        {
            return BuildResult(projectPath, objects,
            [
                new ExportIssue(
                    "OpennessHost",
                    "Out-of-process Openness host is only supported on Windows.",
                    "Run export on Windows with TIA Portal installed.")
            ]);
        }

        var installations = await _installationDiscoveryService.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var preferredInstallation = ResolvePreferredInstallation(installations, tiaInstallationPathOverride);

        if (preferredInstallation is null || string.IsNullOrWhiteSpace(preferredInstallation.InstallPath))
        {
            return BuildResult(projectPath, objects,
            [
                new ExportIssue(
                    "OpennessRuntime",
                    "No supported TIA installation with Openness runtime metadata was detected.",
                    "Install TIA Portal V18/V19/V20, or set manual installation override path.")
            ]);
        }

        var hostPath = ResolveHostExecutablePath();

        if (hostPath is null)
        {
            return BuildResult(projectPath, objects,
            [
                new ExportIssue(
                    "OpennessHost",
                    "Openness host executable was not found.",
                    $"Set environment variable {OutOfProcessHostLocator.HostPathEnvironmentVariable} or deploy TiaProjectExporter.OpennessHost.exe next to the UI executable.")
            ]);
        }

        try
        {
            var response = await ExecuteHostAsync(hostPath, projectPath, preferredInstallation.InstallPath, detailLevel, includedDomains, safetyOfflineProgramPassword, _logger, cancellationToken).ConfigureAwait(false);

            if (response is null)
            {
                return BuildResult(projectPath, objects,
                [
                    new ExportIssue(
                        "OpennessHost",
                        "Openness host did not return a valid traversal response.",
                        "Check host logs and EXPORT_FAILURE.log for details.")
                ]);
            }

            var mappedObjects = response.Objects.Select(MapNode).ToArray();
            var mappedIssues = response.Issues.Select(issue => new ExportIssue(issue.Scope ?? "OpennessTraversal", issue.Message ?? "Unknown issue", issue.Details)).ToArray();

            return new TiaProjectTraversalResult(
                ProjectName: string.IsNullOrWhiteSpace(response.ProjectName) ? Path.GetFileNameWithoutExtension(projectPath) : response.ProjectName,
                ProjectPath: string.IsNullOrWhiteSpace(response.ProjectPath) ? projectPath : response.ProjectPath,
                Objects: mappedObjects,
                Issues: mappedIssues);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Out-of-process Openness traversal failed for project {ProjectPath}", projectPath);

            return BuildResult(projectPath, objects,
            [
                new ExportIssue(
                    "OpennessHost",
                    "Out-of-process Openness traversal failed.",
                    exception.ToString())
            ]);
        }
    }

    private static TiaProjectTraversalResult BuildResult(string projectPath, IReadOnlyList<TiaProjectObjectNode> objects, IReadOnlyList<ExportIssue> issues) =>
        new(
            ProjectName: Path.GetFileNameWithoutExtension(projectPath),
            ProjectPath: projectPath,
            Objects: objects,
            Issues: issues);

    private static TiaProjectObjectNode MapNode(HostObjectNode node) =>
        new(
            ObjectType: string.IsNullOrWhiteSpace(node.ObjectType) ? "UnmappedRuntimeNode" : node.ObjectType,
            Name: string.IsNullOrWhiteSpace(node.Name) ? "Unnamed" : node.Name,
            QualifiedPath: string.IsNullOrWhiteSpace(node.QualifiedPath) ? "Project/Unknown" : node.QualifiedPath,
            Depth: node.Depth,
            Metadata: ParseMetadata(node.Metadata));

    private static Dictionary<string, string> ParseMetadata(JsonElement metadata)
    {
        if (metadata.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (metadata.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in metadata.EnumerateObject())
            {
                result[property.Name] = ConvertJsonValueToString(property.Value);
            }

            return result;
        }

        if (metadata.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in metadata.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryResolveDictionaryEntry(item, out var key, out var value))
                {
                    continue;
                }

                result[key] = value;
            }

            return result;
        }

        result["Value"] = ConvertJsonValueToString(metadata);
        return result;
    }

    private static bool TryResolveDictionaryEntry(JsonElement entry, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        if (entry.TryGetProperty("Key", out var keyElement) && entry.TryGetProperty("Value", out var valueElement))
        {
            key = ConvertJsonValueToString(keyElement);
            value = ConvertJsonValueToString(valueElement);
            return !string.IsNullOrWhiteSpace(key);
        }

        if (entry.TryGetProperty("key", out keyElement) && entry.TryGetProperty("value", out valueElement))
        {
            key = ConvertJsonValueToString(keyElement);
            value = ConvertJsonValueToString(valueElement);
            return !string.IsNullOrWhiteSpace(key);
        }

        return false;
    }

    private static string ConvertJsonValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }

    private static DiscoveredTiaPortalInstallation? ResolvePreferredInstallation(
        IReadOnlyList<DiscoveredTiaPortalInstallation> discoveredInstallations,
        string? tiaInstallationPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(tiaInstallationPathOverride))
        {
            var normalizedPath = tiaInstallationPathOverride.Trim().Trim('"');
            var version = InferVersionFromPath(normalizedPath);

            return new DiscoveredTiaPortalInstallation(
                version,
                $"Manual TIA Override ({version})",
                normalizedPath,
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

    private static string? ResolveHostExecutablePath()
    {
        return OutOfProcessHostLocator.ResolveHostExecutablePath();
    }

    private static async Task<HostTraversalResponse?> ExecuteHostAsync(
        string hostPath,
        string projectPath,
        string installPath,
        TiaTraversalDetailLevel detailLevel,
        IReadOnlyCollection<ExportDomain>? includedDomains,
        string? safetyOfflineProgramPassword,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var arguments = BuildArguments(projectPath, installPath, detailLevel, includedDomains);

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory
        };

        if (!string.IsNullOrWhiteSpace(safetyOfflineProgramPassword))
        {
            startInfo.Environment["TIA_EXPORTER_SAFETY_OFFLINE_PASSWORD"] = safetyOfflineProgramPassword;
        }

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Openness host process.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = ConsumeStandardErrorAsync(process.StandardError, logger, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardErrorResult = await standardErrorTask.ConfigureAwait(false);
        var standardError = standardErrorResult.Aggregated;

        logger.LogInformation("Openness host stderr transcript: {LogPath}", standardErrorResult.LogPath);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Openness host exited with code {process.ExitCode}. STDERR: {standardError}. HostLog: {standardErrorResult.LogPath}");
        }

        if (string.IsNullOrWhiteSpace(standardOutput))
        {
            throw new InvalidOperationException("Openness host returned empty output.");
        }

        var response = JsonSerializer.Deserialize<HostTraversalResponse>(standardOutput, SerializerOptions);

        if (response is null)
        {
            throw new InvalidOperationException("Failed to deserialize Openness host response.");
        }

        return response;
    }

    private static async Task<HostStandardErrorResult> ConsumeStandardErrorAsync(StreamReader errorReader, ILogger logger, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaProjectExporter",
            "HostLogs");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"host-stderr-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");

        await using var stream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await errorReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (TryParseHeartbeat(line, out var heartbeat))
            {
                logger.LogInformation("HostHeartbeat|{Timestamp}|{State}|{Phase}|{Detail}", heartbeat.TimestampUtc, heartbeat.State, heartbeat.Phase, heartbeat.Detail);
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        return new HostStandardErrorResult(builder.ToString(), logPath);
    }

    private static bool TryParseHeartbeat(string line, out HeartbeatMessage heartbeat)
    {
        heartbeat = default;

        if (!line.StartsWith("HB|", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = line.Split('|');

        if (parts.Length < 5)
        {
            return false;
        }

        heartbeat = new HeartbeatMessage(parts[1], parts[2], parts[3], parts[4]);
        return true;
    }

    private static string BuildArguments(
        string projectPath,
        string installPath,
        TiaTraversalDetailLevel detailLevel,
        IReadOnlyCollection<ExportDomain>? includedDomains)
    {
        var builder = new StringBuilder();
        builder.Append("--project ").Append(Quote(projectPath)).Append(' ');
        builder.Append("--install ").Append(Quote(installPath));

        if (includedDomains is { Count: > 0 })
        {
            var serializedDomains = string.Join(
                ',',
                includedDomains
                    .Distinct()
                    .OrderBy(domain => domain.ToString(), StringComparer.Ordinal)
                    .Select(domain => domain.ToString()));

            builder.Append(' ').Append("--domains ").Append(Quote(serializedDomains));
        }

        if (detailLevel == TiaTraversalDetailLevel.Preview)
        {
            builder.Append(' ').Append("--preview");
        }

        return builder.ToString();
    }

    private static string Quote(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "\"\""
            : $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private readonly record struct HostStandardErrorResult(string Aggregated, string LogPath);

    private readonly record struct HeartbeatMessage(string TimestampUtc, string State, string Phase, string Detail);

    private sealed class HostTraversalResponse
    {
        public string? ProjectName { get; set; }

        public string? ProjectPath { get; set; }

        public List<HostObjectNode> Objects { get; set; } = [];

        public List<HostIssue> Issues { get; set; } = [];
    }

    private sealed class HostObjectNode
    {
        public string? ObjectType { get; set; }

        public string? Name { get; set; }

        public string? QualifiedPath { get; set; }

        public int Depth { get; set; }

        [JsonPropertyName("metadata")]
        public JsonElement Metadata { get; set; }
    }

    private sealed class HostIssue
    {
        public string? Scope { get; set; }

        public string? Message { get; set; }

        public string? Details { get; set; }
    }
}
