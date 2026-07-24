using System.Text;
using System.Text.Json;
using TiaProjectExporter.Application;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Core.Models;
using TiaProjectExporter.Infrastructure.Serialization;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Builds file/search indexes and project tree based on actual written artifacts.
/// </summary>
public sealed class ExportIndexStage : IExportStage
{
    /// <inheritdoc />
    public string Name => "Export Indexes";

    /// <inheritdoc />
    public async Task ExecuteAsync(ExportExecutionContext context, CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;

        if (context.Options.Formats.Contains(ExportFormat.Json))
        {
            var fileIndexJson = BuildFileIndexJson(context, generatedAt);
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/FILE_INDEX.json", ExportFormat.Json, fileIndexJson),
                cancellationToken).ConfigureAwait(false);

            var searchIndexJson = BuildSearchIndexJson(context, generatedAt);
            await context.WriteArtifactAsync(
                new ExportArtifact("Export/SEARCH_INDEX.json", ExportFormat.Json, searchIndexJson),
                cancellationToken).ConfigureAwait(false);
        }

        var projectTree = BuildProjectTree(context);
        await context.WriteArtifactAsync(
            new ExportArtifact("Export/PROJECT_TREE.txt", ExportFormat.Markdown, projectTree),
            cancellationToken).ConfigureAwait(false);

        context.AddResult(new ExportedObjectResult("Repository", "Indexes", ExportObjectStatus.Succeeded));
        await context.ReportProgressAsync(new ExportProgressUpdate(Name, "Index files generated", 1, 1, TimeSpan.Zero)).ConfigureAwait(false);
    }

    private static string BuildFileIndexJson(ExportExecutionContext context, DateTimeOffset generatedAt)
    {
        var jsonOptions = JsonOptionsFactory.CreateDefault();

        var files = context.Artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .Select(artifact => new
            {
                artifact.RelativePath,
                Format = artifact.Format.ToString(),
                artifact.ContentLength,
                artifact.WrittenAt
            })
            .ToArray();

        var payload = new
        {
            GeneratedAt = generatedAt,
            FileCount = files.Length,
            Files = files
        };

        return JsonSerializer.Serialize(payload, jsonOptions);
    }

    private static string BuildSearchIndexJson(ExportExecutionContext context, DateTimeOffset generatedAt)
    {
        var jsonOptions = JsonOptionsFactory.CreateDefault();

        var entries = context.Artifacts
            .SelectMany(artifact => TokenizePath(artifact.RelativePath).Select(token => new { Token = token, artifact.RelativePath }))
            .GroupBy(entry => entry.Token, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Token = group.Key,
                Hits = group.Select(item => item.RelativePath).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            })
            .ToArray();

        var payload = new
        {
            GeneratedAt = generatedAt,
            TokenCount = entries.Length,
            Entries = entries
        };

        return JsonSerializer.Serialize(payload, jsonOptions);
    }

    private static string BuildProjectTree(ExportExecutionContext context)
    {
        var lines = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in context.Directories)
        {
            lines.Add(directory.Replace('/', Path.DirectorySeparatorChar));
        }

        foreach (var artifact in context.Artifacts)
        {
            lines.Add(artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private static IEnumerable<string> TokenizePath(string relativePath)
    {
        var sanitized = relativePath
            .Replace('/', ' ')
            .Replace('\\', ' ')
            .Replace('.', ' ')
            .Replace('-', ' ')
            .Replace('_', ' ');

        return sanitized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length >= 2)
            .Select(part => part.ToLowerInvariant());
    }
}
