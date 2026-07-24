using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Maps Siemens Openness runtime nodes to exporter inventory objects for a specific domain.
/// </summary>
public interface ITiaDomainExtractor
{
    /// <summary>
    /// Gets the domain name represented by this extractor.
    /// </summary>
    string Domain { get; }

    /// <summary>
    /// Determines whether this extractor can handle a runtime type name.
    /// </summary>
    bool CanHandle(string runtimeTypeName);

    /// <summary>
    /// Creates an inventory node for a runtime object.
    /// </summary>
    TiaProjectObjectNode? TryExtract(object runtimeNode, string qualifiedPath, int depth);
}
