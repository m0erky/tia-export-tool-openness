using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Produces domain-level export readiness scoring and prioritized improvement actions.
/// </summary>
public sealed class ExportReadinessStage : IExportStage
{
    private static readonly char[] DependencySeparators = [',', ';', '|'];

    private static readonly IReadOnlyDictionary<string, string> RelationshipByMetadataKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Calls"] = "Calls",
        ["BlockCalls"] = "Calls",
        ["InvokedBlocks"] = "Calls",
        ["DependsOn"] = "DependsOn",
        ["Dependencies"] = "DependsOn",
        ["Uses"] = "Uses",
        ["UsesType"] = "Uses",
        ["References"] = "References",
        ["ReferencedTags"] = "UsesTag",
        ["TagUsage"] = "UsesTag"
    };

    /// <inheritdoc />
    public string Name => "Export Readiness";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var report = BuildReport(inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var json = JsonSerializer.Serialize(report.JsonPayload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/EXPORT_READINESS_SCORE.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/EXPORT_READINESS_SCORE.md", ExportFormat.Markdown, BuildMarkdown(report, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "ExportReadiness", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Export readiness scoring generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static ReadinessReport BuildReport(TiaProjectInventory inventory)
    {
        var nodeIds = inventory.Objects
            .Select(BuildNodeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nodeNames = inventory.Objects
            .Select(node => node.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relationships = inventory.Objects
            .SelectMany(source => ParseRelationships(source.Metadata).Select(relation => new RelationshipEdge(
                SourceDomain: ReportDomainCatalog.ResolveDomain(source),
                Target: relation.Target,
                Resolved: RelationshipTargetResolver.IsResolvedTarget(relation.Target, nodeIds, nodeNames),
                Relationship: relation.Relationship)))
            .ToArray();

        var domainScores = ReportDomainCatalog.DomainOrder
            .Select(domain => BuildDomainScore(domain, inventory, relationships))
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Domain, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var prioritized = domainScores
            .Where(entry => entry.PriorityActions.Count > 0)
            .OrderBy(entry => entry.Score)
            .ThenBy(entry => entry.Domain, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(entry => new PrioritizedAction(entry.Domain, entry.Score, entry.PriorityActions.ToArray()))
            .ToArray();

        var overallScore = domainScores.Length == 0
            ? 0
            : (int)Math.Round(domainScores.Average(entry => entry.Score), MidpointRounding.AwayFromZero);

        var payload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            OverallScore = overallScore,
            Domains = domainScores,
            PriorityActions = prioritized
        };

        return new ReadinessReport(overallScore, domainScores, prioritized, payload);
    }

    private static DomainScore BuildDomainScore(string domain, TiaProjectInventory inventory, IReadOnlyList<RelationshipEdge> relationships)
    {
        var nodes = inventory.Objects.Where(node => ReportDomainCatalog.DomainMatches(node, domain)).ToArray();
        var discovered = nodes.Length;

        var confident = nodes.Count(node =>
            node.Metadata is not null
            && node.Metadata.TryGetValue("ExtractionConfidence", out var raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var confidence)
            && confidence >= 0.70);

        var typed = nodes.Count(node =>
            node.Metadata is not null
            && node.Metadata.TryGetValue("ExtractedByTypedExtractor", out var raw)
            && bool.TryParse(raw, out var value)
            && value);

        var fallback = nodes.Count(node =>
            node.Metadata is not null
            && node.Metadata.TryGetValue("FallbackReflectionUsed", out var raw)
            && bool.TryParse(raw, out var value)
            && value);

        var domainRelationships = relationships.Where(edge => edge.SourceDomain.Equals(domain, StringComparison.OrdinalIgnoreCase)).ToArray();
        var unresolved = domainRelationships.Count(edge => !edge.Resolved);

        var issues = ReportDomainCatalog.CountIssuesForDomain(inventory, domain);

        var discoveryScore = discovered == 0 ? 0 : 40;
        var confidenceScore = discovered == 0 ? 0 : (int)Math.Round((confident / (double)discovered) * 20, MidpointRounding.AwayFromZero);
        var typedScore = discovered == 0 ? 0 : (int)Math.Round((typed / (double)discovered) * 20, MidpointRounding.AwayFromZero);
        var fallbackPenalty = discovered == 0 ? 0 : (int)Math.Round((fallback / (double)discovered) * 15, MidpointRounding.AwayFromZero);
        var unresolvedPenalty = domainRelationships.Length == 0
            ? 0
            : (int)Math.Round((unresolved / (double)domainRelationships.Length) * 15, MidpointRounding.AwayFromZero);
        var issuePenalty = Math.Min(10, issues * 2);

        var score = Math.Clamp(discoveryScore + confidenceScore + typedScore - fallbackPenalty - unresolvedPenalty - issuePenalty, 0, 100);
        var supportedByApi = ReportDomainCatalog.SupportedByApiMap.TryGetValue(domain, out var supported) && supported;

        var actions = BuildActions(discovered, confident, typed, fallback, unresolved, issues, supportedByApi);

        return new DomainScore(
            Domain: domain,
            SupportedByApi: supportedByApi,
            Score: score,
            Discovered: discovered,
            HighConfidence: confident,
            Typed: typed,
            Fallback: fallback,
            Relationships: domainRelationships.Length,
            UnresolvedRelationships: unresolved,
            Issues: issues,
            PriorityActions: actions);
    }

    private static IReadOnlyList<string> BuildActions(int discovered, int confident, int typed, int fallback, int unresolved, int issues, bool supportedByApi)
    {
        var actions = new List<string>();

        if (!supportedByApi)
        {
            actions.Add("API support is limited; keep this domain low-priority for readiness improvements.");
            return actions;
        }

        if (discovered == 0)
        {
            actions.Add("No objects discovered; extend traversal and typed extractor entry points for this domain.");
        }

        if (discovered > 0 && confident < discovered)
        {
            actions.Add("Increase extraction confidence by mapping Siemens runtime properties directly for key object types.");
        }

        if (discovered > 0 && typed < discovered)
        {
            actions.Add("Replace reflection fallback nodes with explicit typed extractor mappings.");
        }

        if (fallback > 0)
        {
            actions.Add("Prioritize fallback hotspots and map top runtime types first.");
        }

        if (unresolved > 0)
        {
            actions.Add("Resolve dependency targets via Siemens identifiers to reduce unresolved relationships.");
        }

        if (issues > 0)
        {
            actions.Add("Address recurring domain issues from export report hotspots.");
        }

        return actions;
    }

    private static string BuildMarkdown(ReadinessReport report, TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Export Readiness Score");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine($"Overall readiness score: **{report.OverallScore}/100**");
        builder.AppendLine();
        builder.AppendLine("## Domain Scores");
        builder.AppendLine();
        builder.AppendLine("| Domain | Score | Discovered | High Conf. | Typed | Fallback | Unresolved | Issues |");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var entry in report.Domains.OrderByDescending(entry => entry.Score).ThenBy(entry => entry.Domain, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"| {entry.Domain} | {entry.Score} | {entry.Discovered} | {entry.HighConfidence} | {entry.Typed} | {entry.Fallback} | {entry.UnresolvedRelationships} | {entry.Issues} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Priority Actions");
        builder.AppendLine();

        if (report.PriorityActions.Count == 0)
        {
            builder.AppendLine("No priority actions detected.");
            return builder.ToString();
        }

        foreach (var action in report.PriorityActions)
        {
            builder.AppendLine($"### {action.Domain} (Score: {action.Score})");
            builder.AppendLine();

            foreach (var item in action.Actions)
            {
                builder.AppendLine($"- {item}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IReadOnlyList<DependencyRelation> ParseRelationships(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<DependencyRelation>();
        }

        return RelationshipByMetadataKey
            .Where(entry => metadata.ContainsKey(entry.Key))
            .SelectMany(entry => SplitValues(metadata[entry.Key]).Select(target => new DependencyRelation(target, entry.Value)))
            .DistinctBy(entry => $"{entry.Target}|{entry.Relationship}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SplitValues(string raw)
    {
        return raw
            .Split(DependencySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(RelationshipTargetResolver.NormalizeTarget)
            .Where(token => !string.IsNullOrWhiteSpace(token));
    }

    private static string BuildNodeId(TiaProjectObjectNode node)
    {
        var seed = string.IsNullOrWhiteSpace(node.QualifiedPath) ? node.Name : node.QualifiedPath;
        return seed.Trim().Replace(' ', '_');
    }

    private sealed record DependencyRelation(string Target, string Relationship);

    private sealed record RelationshipEdge(string SourceDomain, string Target, bool Resolved, string Relationship);

    private sealed record DomainScore(
        string Domain,
        bool SupportedByApi,
        int Score,
        int Discovered,
        int HighConfidence,
        int Typed,
        int Fallback,
        int Relationships,
        int UnresolvedRelationships,
        int Issues,
        IReadOnlyList<string> PriorityActions);

    private sealed record PrioritizedAction(string Domain, int Score, IReadOnlyList<string> Actions);

    private sealed record ReadinessReport(
        int OverallScore,
        IReadOnlyList<DomainScore> Domains,
        IReadOnlyList<PrioritizedAction> PriorityActions,
        object JsonPayload);
}
