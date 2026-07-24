using System.IO;
using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Tracks typed-extractor mapping implementation progress and computes trend against previous runs.
/// </summary>
public sealed class MappingImplementationTrackerStage : IExportStage
{
    private const string HistoryRelativePath = "Export/Reports/MAPPING_IMPLEMENTATION_TRACKER_HISTORY.json";
    private const int MaxHistoryEntries = 40;

    /// <inheritdoc />
    public string Name => "Mapping Implementation Tracker";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var mappings = BuildMappingEntries(inventory).ToArray();
        var snapshot = BuildSnapshot(mappings, now);

        var historyPath = Path.Combine(context.Options.OutputDirectory, HistoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var history = await LoadHistoryAsync(historyPath, cancellationToken).ConfigureAwait(false);
        var previous = history.LastOrDefault();

        var trend = BuildTrend(previous, snapshot);
        history.Add(snapshot);

        if (history.Count > MaxHistoryEntries)
        {
            history = history.Skip(history.Count - MaxHistoryEntries).ToList();
        }

        await PersistHistoryAsync(historyPath, history, cancellationToken).ConfigureAwait(false);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var payload = new
            {
                GeneratedAt = now,
                inventory.ProjectName,
                Status = inventory.Status.ToString(),
                Snapshot = snapshot,
                Trend = trend,
                Mappings = mappings
            };

            await context.WriteArtifactAsync(
                new ExportArtifact(
                    "Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.json",
                    ExportFormat.Json,
                    JsonSerializer.Serialize(payload, JsonOptionsFactory.CreateDefault())),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact(
                    "Export/Reports/MAPPING_IMPLEMENTATION_TRACKER.md",
                    ExportFormat.Markdown,
                    BuildMarkdown(snapshot, trend, mappings, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "MappingImplementationTracker", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Mapping implementation tracker generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static IEnumerable<MappingEntry> BuildMappingEntries(TiaProjectInventory inventory)
    {
        var candidates = inventory.Objects
            .Where(node => node.Metadata is not null)
            .Select(node =>
            {
                var metadata = node.Metadata!;
                if (!metadata.TryGetValue("RuntimeType", out var runtimeType) || string.IsNullOrWhiteSpace(runtimeType))
                {
                    return null;
                }

                var domain = metadata.TryGetValue("Domain", out var domainValue) && !string.IsNullOrWhiteSpace(domainValue)
                    ? domainValue
                    : "Unknown";

                var typedExtractor = metadata.TryGetValue("TypedExtractor", out var extractor)
                    ? extractor
                    : string.Empty;

                var fallbackUsed = metadata.TryGetValue("FallbackReflectionUsed", out var rawFallback)
                    && bool.TryParse(rawFallback, out var fallback)
                    && fallback;

                var isMapped = !string.IsNullOrWhiteSpace(typedExtractor) && !fallbackUsed;

                return new RawEntry(runtimeType, domain, node.ObjectType, typedExtractor, fallbackUsed, isMapped);
            })
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToArray();

        return candidates
            .GroupBy(entry => new { entry.RuntimeType, entry.Domain, entry.ObjectType, entry.TypedExtractor, entry.FallbackUsed, entry.IsMapped })
            .Select(group => new MappingEntry(
                group.Key.RuntimeType,
                group.Key.Domain,
                group.Key.ObjectType,
                group.Key.TypedExtractor,
                group.Key.FallbackUsed,
                group.Key.IsMapped,
                group.Count()))
            .OrderByDescending(entry => entry.ObservedCount)
            .ThenBy(entry => entry.RuntimeType, StringComparer.OrdinalIgnoreCase);
    }

    private static TrackerSnapshot BuildSnapshot(IReadOnlyCollection<MappingEntry> mappings, DateTimeOffset generatedAt)
    {
        var uniqueRuntimeTypes = mappings
            .Select(entry => entry.RuntimeType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var mappedRuntimeTypes = mappings
            .Where(entry => entry.IsMapped)
            .Select(entry => entry.RuntimeType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var fallbackRuntimeTypes = mappings
            .Where(entry => entry.FallbackUsed)
            .Select(entry => entry.RuntimeType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var completionRate = uniqueRuntimeTypes == 0
            ? 0
            : Math.Round((mappedRuntimeTypes / (double)uniqueRuntimeTypes) * 100, 2);

        return new TrackerSnapshot(
            generatedAt,
            uniqueRuntimeTypes,
            mappedRuntimeTypes,
            fallbackRuntimeTypes,
            completionRate);
    }

    private static TrackerTrend BuildTrend(TrackerSnapshot? previous, TrackerSnapshot current)
    {
        if (previous is null)
        {
            return new TrackerTrend(false, 0, 0, 0, 0);
        }

        return new TrackerTrend(
            HasPreviousSnapshot: true,
            CompletionRateDelta: Math.Round(current.CompletionRate - previous.CompletionRate, 2),
            MappedRuntimeTypesDelta: current.MappedRuntimeTypes - previous.MappedRuntimeTypes,
            FallbackRuntimeTypesDelta: current.FallbackRuntimeTypes - previous.FallbackRuntimeTypes,
            UniqueRuntimeTypesDelta: current.UniqueRuntimeTypes - previous.UniqueRuntimeTypes);
    }

    private static async Task<List<TrackerSnapshot>> LoadHistoryAsync(string historyPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(historyPath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(historyPath, cancellationToken).ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<List<TrackerSnapshot>>(json, JsonOptionsFactory.CreateDefault());
            return data ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task PersistHistoryAsync(string historyPath, IReadOnlyList<TrackerSnapshot> history, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(historyPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(history, JsonOptionsFactory.CreateDefault());
        await File.WriteAllTextAsync(historyPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildMarkdown(
        TrackerSnapshot snapshot,
        TrackerTrend trend,
        IReadOnlyCollection<MappingEntry> mappings,
        TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Mapping Implementation Tracker");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine($"Generated: `{snapshot.GeneratedAt:O}`");
        builder.AppendLine();
        builder.AppendLine("## Snapshot");
        builder.AppendLine();
        builder.AppendLine($"- Unique runtime types: **{snapshot.UniqueRuntimeTypes}**");
        builder.AppendLine($"- Mapped runtime types: **{snapshot.MappedRuntimeTypes}**");
        builder.AppendLine($"- Fallback runtime types: **{snapshot.FallbackRuntimeTypes}**");
        builder.AppendLine($"- Mapping completion rate: **{snapshot.CompletionRate:0.00}%**");
        builder.AppendLine();
        builder.AppendLine("## Trend");
        builder.AppendLine();

        if (!trend.HasPreviousSnapshot)
        {
            builder.AppendLine("No previous snapshot found; trend will appear from the next run onward.");
        }
        else
        {
            builder.AppendLine($"- Completion rate delta: **{trend.CompletionRateDelta:+0.00;-0.00;0.00}%**");
            builder.AppendLine($"- Mapped runtime types delta: **{trend.MappedRuntimeTypesDelta:+#;-#;0}**");
            builder.AppendLine($"- Fallback runtime types delta: **{trend.FallbackRuntimeTypesDelta:+#;-#;0}**");
            builder.AppendLine($"- Unique runtime types delta: **{trend.UniqueRuntimeTypesDelta:+#;-#;0}**");
        }

        builder.AppendLine();
        builder.AppendLine("## Mapping Entries");
        builder.AppendLine();
        builder.AppendLine("| Runtime Type | Domain | Object | Mapped | Fallback | Extractor | Observed | ");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | ---: |");

        foreach (var entry in mappings.Take(300))
        {
            builder.AppendLine($"| {entry.RuntimeType} | {entry.Domain} | {entry.ObjectType} | {entry.IsMapped} | {entry.FallbackUsed} | {(string.IsNullOrWhiteSpace(entry.TypedExtractor) ? "-" : entry.TypedExtractor)} | {entry.ObservedCount} |");
        }

        return builder.ToString();
    }

    private sealed record RawEntry(
        string RuntimeType,
        string Domain,
        string ObjectType,
        string TypedExtractor,
        bool FallbackUsed,
        bool IsMapped);

    private sealed record MappingEntry(
        string RuntimeType,
        string Domain,
        string ObjectType,
        string TypedExtractor,
        bool FallbackUsed,
        bool IsMapped,
        int ObservedCount);

    private sealed record TrackerSnapshot(
        DateTimeOffset GeneratedAt,
        int UniqueRuntimeTypes,
        int MappedRuntimeTypes,
        int FallbackRuntimeTypes,
        double CompletionRate);

    private sealed record TrackerTrend(
        bool HasPreviousSnapshot,
        double CompletionRateDelta,
        int MappedRuntimeTypesDelta,
        int FallbackRuntimeTypesDelta,
        int UniqueRuntimeTypesDelta);
}
