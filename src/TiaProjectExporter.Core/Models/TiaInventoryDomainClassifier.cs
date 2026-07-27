namespace TiaProjectExporter.Core.Models;

/// <summary>
/// Classifies inventory nodes into export domains.
/// </summary>
public static class TiaInventoryDomainClassifier
{
    /// <summary>
    /// Resolves the target export domain for an inventory node.
    /// </summary>
    public static ExportDomain ResolveDomain(TiaProjectObjectNode node)
    {
        if (IsBlockObjectType(node.ObjectType))
        {
            return ExportDomain.Blocks;
        }

        if (IsTagObjectType(node.ObjectType))
        {
            return ExportDomain.Tags;
        }

        if (IsUdtObjectType(node.ObjectType))
        {
            return ExportDomain.Udts;
        }

        if (IsHmiObjectType(node.ObjectType))
        {
            return ExportDomain.Hmi;
        }

        var candidate = $"{node.ObjectType} {node.QualifiedPath} {node.Name}";

        if (IsProjectRootNode(node))
        {
            return ExportDomain.Project;
        }

        if (ContainsAny(candidate, "Device", "Module", "Rack", "Hardware", "Cpu"))
        {
            return ExportDomain.Hardware;
        }

        if (ContainsAny(candidate, "Network", "Profinet", "Profibus", "Connection", "Subnet", "Port", "Interface"))
        {
            return ExportDomain.Network;
        }

        if (ContainsAny(candidate, "Technology", "Motion", "Pid", "Safety"))
        {
            return ExportDomain.Technology;
        }

        if (ContainsAny(candidate, "Library", "MasterCopy"))
        {
            return ExportDomain.Libraries;
        }

        if (ContainsAny(candidate, "Diagnostic", "Audit", "User", "Health", "Alarm"))
        {
            return ExportDomain.Diagnostics;
        }

        if (ContainsAny(candidate, "Plc", "Software", "Program"))
        {
            return ExportDomain.Plc;
        }

        return ExportDomain.Metadata;
    }

    /// <summary>
    /// Resolves a stable folder segment for a domain.
    /// </summary>
    public static string ToFolderName(ExportDomain domain)
    {
        return domain switch
        {
            ExportDomain.Project => "Project",
            ExportDomain.Hardware => "Hardware",
            ExportDomain.Network => "Network",
            ExportDomain.Plc => "PLC",
            ExportDomain.Blocks => "Blocks",
            ExportDomain.Tags => "Tags",
            ExportDomain.Udts => "UDTs",
            ExportDomain.Technology => "Technology",
            ExportDomain.Libraries => "Libraries",
            ExportDomain.Hmi => "HMI",
            ExportDomain.Diagnostics => "Diagnostics",
            _ => "Metadata"
        };
    }

    private static bool IsBlockObjectType(string objectType)
    {
        return objectType is "OB" or "FB" or "FC" or "DB" or "InstanceDB" or "FunctionBlock" or "Function" or "OrganizationBlock" or "DataBlock" or "Source";
    }

    private static bool IsTagObjectType(string objectType)
    {
        return objectType is "Tag" or "TagTable";
    }

    private static bool IsUdtObjectType(string objectType)
    {
        return objectType is "UDT" or "DataType" or "Type";
    }

    private static bool IsHmiObjectType(string objectType)
    {
        return objectType.Equals("HmiObject", StringComparison.OrdinalIgnoreCase)
            || objectType.Contains("Hmi", StringComparison.OrdinalIgnoreCase)
            || objectType.Contains("Screen", StringComparison.OrdinalIgnoreCase)
            || objectType.Contains("Faceplate", StringComparison.OrdinalIgnoreCase)
            || objectType.Contains("Recipe", StringComparison.OrdinalIgnoreCase)
            || objectType.Contains("Archive", StringComparison.OrdinalIgnoreCase)
            || objectType.Contains("Script", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string candidate, params string[] terms)
    {
        return terms.Any(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProjectRootNode(TiaProjectObjectNode node)
    {
        if (node.Depth == 0)
        {
            return true;
        }

        if (!string.Equals(node.QualifiedPath, "Project", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return node.ObjectType.Contains("Project", StringComparison.OrdinalIgnoreCase)
            || node.ObjectType.Contains("Root", StringComparison.OrdinalIgnoreCase);
    }
}
