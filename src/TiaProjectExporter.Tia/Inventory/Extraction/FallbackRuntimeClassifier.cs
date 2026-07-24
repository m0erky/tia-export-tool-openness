namespace TiaProjectExporter.Tia.Inventory.Extraction;

/// <summary>
/// Classifies reflection-only runtime nodes into coarse export domains.
/// </summary>
public static class FallbackRuntimeClassifier
{
    /// <summary>
    /// Classifies an unmapped runtime type into a fallback domain/object type.
    /// </summary>
    public static FallbackRuntimeClassification? Classify(string runtimeTypeName, string qualifiedPath)
    {
        if (Matches(runtimeTypeName, qualifiedPath, "network", "profinet", "profibus", "subnet", "iosystem"))
        {
            return new FallbackRuntimeClassification("Network", "UnmappedNetworkNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "hardware", "device", "rack", "module", "gsd"))
        {
            return new FallbackRuntimeClassification("Hardware", "UnmappedHardwareNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "hmi", "wincc", "screen", "faceplate", "alarm", "recipe", "script", "archive"))
        {
            return new FallbackRuntimeClassification("HMI", "UnmappedHmiNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "technology", "motion", "pid", "safety", "axis", "cam"))
        {
            return new FallbackRuntimeClassification("Technology", "UnmappedTechnologyNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "library", "mastercopy", "typeversion"))
        {
            return new FallbackRuntimeClassification("Libraries", "UnmappedLibraryNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "diagnostic", "trace", "online", "event"))
        {
            return new FallbackRuntimeClassification("Diagnostics", "UnmappedDiagnosticNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "user", "role", "permission", "audit", "security"))
        {
            return new FallbackRuntimeClassification("UsersAudit", "UnmappedUsersAuditNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "plc", "program", "block", "tag", "udt", "datatype", "software"))
        {
            return new FallbackRuntimeClassification("PLC", "UnmappedPlcNode");
        }

        if (Matches(runtimeTypeName, qualifiedPath, "project", "group", "folder", "node", "container", "tree"))
        {
            return new FallbackRuntimeClassification("Project", "UnmappedProjectNode");
        }

        return null;
    }

    private static bool Matches(string runtimeTypeName, string qualifiedPath, params string[] terms)
    {
        var candidate = $"{runtimeTypeName} {qualifiedPath}";
        return terms.Any(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Classification details for a reflection fallback runtime node.
/// </summary>
public sealed record FallbackRuntimeClassification(string Domain, string ObjectType);
