using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Generates a single prioritized action plan by combining readiness, fallback, and unresolved relationship signals.
/// </summary>
public sealed class NextBestActionsStage : IExportStage
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
    public string Name => "Next Best Actions";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var inventory = context.Inventory;

        if (inventory is null)
        {
            return;
        }

        var plan = BuildPlan(inventory);

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var json = JsonSerializer.Serialize(plan.JsonPayload, JsonOptionsFactory.CreateDefault());
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/NEXT_BEST_ACTIONS.json", ExportFormat.Json, json),
                cancellationToken).ConfigureAwait(false);
        }

        if (context.Options.GenerateMarkdownSummaries && context.Options.Formats.Contains(ExportFormat.Markdown))
        {
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/Reports/NEXT_BEST_ACTIONS.md", ExportFormat.Markdown, BuildMarkdown(plan, inventory)),
                cancellationToken).ConfigureAwait(false);
        }

        context.AddResult(new ExportedObjectResult("Analysis", "NextBestActions", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Next-best action plan generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static ActionPlan BuildPlan(TiaProjectInventory inventory)
    {
        var nodeIds = inventory.Objects
            .Select(BuildNodeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nodeNames = inventory.Objects
            .Select(node => node.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unresolvedByDomain = inventory.Objects
            .SelectMany(source => ParseRelationships(source.Metadata).Select(relation => new
            {
                Domain = ReportDomainCatalog.ResolveDomain(source),
                relation.Target,
                Resolved = RelationshipTargetResolver.IsResolvedTarget(relation.Target, nodeIds, nodeNames)
            }))
            .Where(entry => !entry.Resolved)
            .GroupBy(entry => entry.Domain)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(entry => entry.Target)
                    .OrderByDescending(inner => inner.Count())
                    .ThenBy(inner => inner.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(10)
                    .Select(inner => new Hotspot(inner.Key, inner.Count()))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var readiness = ReportDomainCatalog.DomainOrder
            .Select(domain => BuildDomainInput(domain, inventory, unresolvedByDomain))
            .ToArray();

        var actions = readiness
            .SelectMany(input => BuildActions(input))
            .OrderByDescending(action => action.ImpactScore)
            .ThenBy(action => action.Domain, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        var payload = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            inventory.ProjectName,
            Status = inventory.Status.ToString(),
            Summary = new
            {
                TotalActions = actions.Length,
                HighImpactActions = actions.Count(action => action.ImpactScore >= 70),
                DomainsWithActions = actions.Select(action => action.Domain).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            },
            Actions = actions,
            DomainInputs = readiness
        };

        return new ActionPlan(actions, readiness, payload);
    }

    private static DomainInput BuildDomainInput(
        string domain,
        TiaProjectInventory inventory,
        IReadOnlyDictionary<string, Hotspot[]> unresolvedByDomain)
    {
        var nodes = inventory.Objects.Where(node => ReportDomainCatalog.DomainMatches(node, domain)).ToArray();
        var discovered = nodes.Length;
        var typed = nodes.Count(node => IsTrue(node.Metadata, "ExtractedByTypedExtractor"));
        var fallback = nodes.Count(node => IsTrue(node.Metadata, "FallbackReflectionUsed"));
        var lowConfidence = nodes.Count(node =>
            node.Metadata is null
            || !node.Metadata.TryGetValue("ExtractionConfidence", out var raw)
            || !double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var score)
            || score < 0.70);

        var issues = ReportDomainCatalog.CountIssuesForDomain(inventory, domain);

        unresolvedByDomain.TryGetValue(domain, out var unresolvedHotspots);

        return new DomainInput(
            Domain: domain,
            Discovered: discovered,
            Typed: typed,
            Fallback: fallback,
            LowConfidence: lowConfidence,
            Issues: issues,
            UnresolvedTargets: unresolvedHotspots ?? Array.Empty<Hotspot>());
    }

    private static IEnumerable<ActionItem> BuildActions(DomainInput input)
    {
        if (input.Discovered == 0)
        {
            yield return new ActionItem(
                Domain: input.Domain,
                Category: "Discovery",
                ImpactScore: 85,
                Task: "Extend traversal and typed entry points to discover missing domain objects.",
                Evidence: "No objects discovered.");
        }

        if (input.Fallback > 0)
        {
            var impact = Math.Min(95, 50 + (input.Fallback * 4));
            yield return new ActionItem(
                Domain: input.Domain,
                Category: "FallbackReduction",
                ImpactScore: impact,
                Task: "Map top fallback runtime types with explicit typed extractors.",
                Evidence: $"Fallback objects: {input.Fallback}.");
        }

        if (input.LowConfidence > 0)
        {
            var impact = Math.Min(90, 45 + (input.LowConfidence * 3));
            yield return new ActionItem(
                Domain: input.Domain,
                Category: "Confidence",
                ImpactScore: impact,
                Task: "Increase extraction confidence by reading Siemens-native properties for key objects.",
                Evidence: $"Low-confidence objects: {input.LowConfidence}.");
        }

        if (input.UnresolvedTargets.Count > 0)
        {
            var top = input.UnresolvedTargets[0];
            var impact = Math.Min(95, 55 + (top.Count * 5));
            yield return new ActionItem(
                Domain: input.Domain,
                Category: "RelationshipResolution",
                ImpactScore: impact,
                Task: "Resolve relationship targets via stable Siemens identifiers (not name heuristics).",
                Evidence: $"Top unresolved target: {top.Name} ({top.Count}).");
        }

        if (input.Issues > 0)
        {
            var impact = Math.Min(85, 40 + (input.Issues * 4));
            yield return new ActionItem(
                Domain: input.Domain,
                Category: "Reliability",
                ImpactScore: impact,
                Task: "Address recurring export issues and exception hotspots for this domain.",
                Evidence: $"Reported issues: {input.Issues}.");
        }
    }

    private static string BuildMarkdown(ActionPlan plan, TiaProjectInventory inventory)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Next Best Actions");
        builder.AppendLine();
        builder.AppendLine($"Project: `{inventory.ProjectName ?? "Not available"}`");
        builder.AppendLine();
        builder.AppendLine($"Inventory status: **{inventory.Status}**");
        builder.AppendLine();
        builder.AppendLine($"Generated actions: **{plan.Actions.Count}**");
        builder.AppendLine();

        if (plan.Actions.Count == 0)
        {
            builder.AppendLine("No high-impact actions identified with current signals.");
            return builder.ToString();
        }

        builder.AppendLine("## Prioritized Actions");
        builder.AppendLine();

        foreach (var action in plan.Actions)
        {
            builder.AppendLine($"- [{action.ImpactScore}] {action.Domain} / {action.Category}: {action.Task}");
            builder.AppendLine($"  - Evidence: {action.Evidence}");
        }

        builder.AppendLine();
        builder.AppendLine("## Domain Signal Summary");
        builder.AppendLine();
        builder.AppendLine("| Domain | Discovered | Typed | Fallback | Low Conf. | Issues | Top Unresolved | ");
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");

        foreach (var input in plan.DomainInputs.OrderByDescending(input => input.Fallback + input.LowConfidence + input.Issues))
        {
            var topUnresolved = input.UnresolvedTargets.FirstOrDefault();
            var unresolvedText = topUnresolved is null ? "-" : $"{topUnresolved.Name} ({topUnresolved.Count})";
            builder.AppendLine($"| {input.Domain} | {input.Discovered} | {input.Typed} | {input.Fallback} | {input.LowConfidence} | {input.Issues} | {unresolvedText} |");
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

    private static bool IsTrue(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null
        && metadata.TryGetValue(key, out var raw)
        && bool.TryParse(raw, out var value)
        && value;

    private static string BuildNodeId(TiaProjectObjectNode node)
    {
        var seed = string.IsNullOrWhiteSpace(node.QualifiedPath) ? node.Name : node.QualifiedPath;
        return seed.Trim().Replace(' ', '_');
    }

    private sealed record DependencyRelation(string Target, string Relationship);

    private sealed record Hotspot(string Name, int Count);

    private sealed record DomainInput(
        string Domain,
        int Discovered,
        int Typed,
        int Fallback,
        int LowConfidence,
        int Issues,
        IReadOnlyList<Hotspot> UnresolvedTargets);

    private sealed record ActionItem(
        string Domain,
        string Category,
        int ImpactScore,
        string Task,
        string Evidence);

    private sealed record ActionPlan(
        IReadOnlyList<ActionItem> Actions,
        IReadOnlyList<DomainInput> DomainInputs,
        object JsonPayload);
}
