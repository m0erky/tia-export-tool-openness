using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Extracts diagnostics-related runtime objects.
/// </summary>
public sealed class DiagnosticsDomainExtractor : ITiaDomainExtractor
{
    /// <inheritdoc />
    public string Domain => "Diagnostics";

    /// <inheritdoc />
    public bool CanHandle(string runtimeTypeName) =>
        runtimeTypeName.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Alarm", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Event", StringComparison.OrdinalIgnoreCase)
        || runtimeTypeName.Contains("Message", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth)
    {
        if (!CanHandle(runtimeNode.GetType().Name))
        {
            return null;
        }

        var objectType = runtimeNode.GetType().Name.Contains("Alarm", StringComparison.OrdinalIgnoreCase)
            ? "Alarm"
            : runtimeNode.GetType().Name.Contains("Event", StringComparison.OrdinalIgnoreCase)
                ? "DiagnosticEvent"
                : "Diagnostic";

        var name = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Name")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "DisplayName")
            ?? runtimeNode.GetType().Name;

        var metadata = new Dictionary<string, string>(ReflectionNodeIntrospection.BuildCommonMetadata(runtimeNode, objectType), StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = Domain
        };

        var severity = ReflectionNodeIntrospection.TryReadString(runtimeNode, "Severity")
            ?? ReflectionNodeIntrospection.TryReadString(runtimeNode, "Class");
        if (!string.IsNullOrWhiteSpace(severity))
        {
            metadata["Severity"] = severity;
        }

        return new TiaProjectObjectNode(objectType, name, qualifiedPath, depth, metadata);
    }
}
