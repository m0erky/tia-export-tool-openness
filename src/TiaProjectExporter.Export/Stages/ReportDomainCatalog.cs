using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.Export.Stages;

/// <summary>
/// Shared domain mapping/metrics for report stages to keep discovered counts consistent.
/// </summary>
internal static class ReportDomainCatalog
{
    public static readonly IReadOnlyDictionary<string, bool> SupportedByApiMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
        ["Project"] = true,
        ["Hardware"] = true,
        ["Network"] = true,
        ["PLC.Blocks"] = true,
        ["PLC.Tags"] = true,
        ["PLC.DataTypes"] = true,
        ["HMI"] = true,
        ["Libraries"] = true,
        ["Diagnostics"] = true,
        ["Technology"] = true,
        ["Metadata"] = false,
        ["UsersAudit"] = true
    };

    public static readonly string[] DomainOrder =
    [
        "Project",
        "Hardware",
        "Network",
        "PLC.Blocks",
        "PLC.Tags",
        "PLC.DataTypes",
        "HMI",
        "Libraries",
        "Diagnostics",
        "Technology",
        "Metadata",
        "UsersAudit"
    ];

    public static string ResolveDomain(TiaProjectObjectNode node)
    {
        if (node.Metadata is not null
            && node.Metadata.TryGetValue("Domain", out var metadataDomain)
            && !string.IsNullOrWhiteSpace(metadataDomain))
        {
            var normalized = NormalizeDomain(metadataDomain.Trim(), node);
            if (IsKnownDomain(normalized))
            {
                return normalized;
            }
        }

        if (node.Metadata is not null
            && node.Metadata.TryGetValue("TypedExtractor", out var extractor)
            && !string.IsNullOrWhiteSpace(extractor))
        {
            var mapped = MapTypedExtractorDomain(extractor);
            if (mapped is not null)
            {
                return mapped;
            }
        }

        var candidate = $"{node.ObjectType} {node.QualifiedPath} {node.Name}";

        if (IsProjectNode(node, candidate))
        {
            return "Project";
        }

        if (IsBlocksNode(node, candidate))
        {
            return "PLC.Blocks";
        }

        if (IsTagsNode(node, candidate))
        {
            return "PLC.Tags";
        }

        if (IsDataTypesNode(node, candidate))
        {
            return "PLC.DataTypes";
        }

        if (ContainsAny(candidate, "Hmi", "Screen", "Faceplate", "Wincc", "Recipe", "Archive", "Script"))
        {
            return "HMI";
        }

        if (ContainsAny(candidate, "Library", "MasterCopy"))
        {
            return "Libraries";
        }

        if (ContainsAny(candidate, "Technology", "Motion", "Pid", "Safety", "Axis", "Cam"))
        {
            return "Technology";
        }

        if (ContainsAny(candidate, "Diagnostic", "Alarm", "Trace"))
        {
            return "Diagnostics";
        }

        if (ContainsAny(candidate, "Audit", "User", "Role", "Permission", "Security"))
        {
            return "UsersAudit";
        }

        if (ContainsAny(candidate, "Network", "Profinet", "Profibus", "Subnet", "IoSystem", "Interface", "Port")
            || node.QualifiedPath.Contains("/Network/", StringComparison.OrdinalIgnoreCase))
        {
            return "Network";
        }

        if (ContainsAny(candidate, "Device", "Rack", "Module", "Cpu", "Hardware", "HwIdentifier", "DeviceItemImpl", "Address")
            || node.QualifiedPath.Contains("/Devices/", StringComparison.OrdinalIgnoreCase))
        {
            return "Hardware";
        }

        return "Metadata";
    }

    public static bool DomainMatches(TiaProjectObjectNode node, string domain) =>
        ResolveDomain(node).Equals(domain, StringComparison.OrdinalIgnoreCase);

    public static int CountIssuesForDomain(TiaProjectInventory inventory, string domain)
    {
        var aliases = GetDomainAliases(domain);
        return inventory.Issues.Count(issue => aliases.Any(alias =>
            issue.Scope.Contains(alias, StringComparison.OrdinalIgnoreCase)
            || issue.Message.Contains(alias, StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizeDomain(string metadataDomain, TiaProjectObjectNode node)
    {
        if (metadataDomain.Equals("Blocks", StringComparison.OrdinalIgnoreCase))
        {
            return "PLC.Blocks";
        }

        if (metadataDomain.Equals("Tags", StringComparison.OrdinalIgnoreCase))
        {
            return "PLC.Tags";
        }

        if (metadataDomain.Equals("Udts", StringComparison.OrdinalIgnoreCase)
            || metadataDomain.Equals("UDTs", StringComparison.OrdinalIgnoreCase)
            || metadataDomain.Equals("PLC.UDTs", StringComparison.OrdinalIgnoreCase)
            || metadataDomain.Equals("PLC.DataType", StringComparison.OrdinalIgnoreCase))
        {
            return "PLC.DataTypes";
        }

        if (metadataDomain.Equals("Plc", StringComparison.OrdinalIgnoreCase)
            || metadataDomain.Equals("PLC", StringComparison.OrdinalIgnoreCase))
        {
            if (IsBlocksNode(node, $"{node.ObjectType} {node.QualifiedPath} {node.Name}"))
            {
                return "PLC.Blocks";
            }

            if (IsTagsNode(node, $"{node.ObjectType} {node.QualifiedPath} {node.Name}"))
            {
                return "PLC.Tags";
            }

            if (IsDataTypesNode(node, $"{node.ObjectType} {node.QualifiedPath} {node.Name}"))
            {
                return "PLC.DataTypes";
            }

            return "Metadata";
        }

        return metadataDomain;
    }

    private static bool IsKnownDomain(string domain) =>
        DomainOrder.Contains(domain, StringComparer.OrdinalIgnoreCase);

    private static string? MapTypedExtractorDomain(string extractor)
    {
        return extractor switch
        {
            var name when name.Contains("PlcBlock", StringComparison.OrdinalIgnoreCase) => "PLC.Blocks",
            var name when name.Contains("PlcTag", StringComparison.OrdinalIgnoreCase) => "PLC.Tags",
            var name when name.Contains("PlcDataType", StringComparison.OrdinalIgnoreCase) => "PLC.DataTypes",
            var name when name.Contains("Hardware", StringComparison.OrdinalIgnoreCase) => "Hardware",
            var name when name.Contains("Network", StringComparison.OrdinalIgnoreCase) => "Network",
            var name when name.Contains("Hmi", StringComparison.OrdinalIgnoreCase) => "HMI",
            var name when name.Contains("Library", StringComparison.OrdinalIgnoreCase) => "Libraries",
            var name when name.Contains("Diagnostic", StringComparison.OrdinalIgnoreCase) => "Diagnostics",
            var name when name.Contains("Technology", StringComparison.OrdinalIgnoreCase) => "Technology",
            var name when name.Contains("UsersAudit", StringComparison.OrdinalIgnoreCase) => "UsersAudit",
            var name when name.Contains("ProjectHierarchy", StringComparison.OrdinalIgnoreCase) => "Project",
            var name when name.Contains("Metadata", StringComparison.OrdinalIgnoreCase) => "Metadata",
            _ => null
        };
    }

    private static bool IsProjectNode(TiaProjectObjectNode node, string candidate)
    {
        if (string.Equals(node.QualifiedPath, "Project", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return node.ObjectType.Equals("Project", StringComparison.OrdinalIgnoreCase)
            || node.ObjectType.Equals("ProjectMetadata", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(candidate, "ProjectMetadata", "ProjectRoot", "ProjectInfo");
    }

    private static bool IsBlocksNode(TiaProjectObjectNode node, string candidate)
    {
        return node.ObjectType is "OB" or "FB" or "FC" or "DB" or "Block" or "InstanceDB" or "FunctionBlock" or "Function" or "OrganizationBlock" or "DataBlock" or "Source"
            || node.QualifiedPath.Contains("/BlockGroup", StringComparison.OrdinalIgnoreCase)
            || node.QualifiedPath.Contains("/Blocks/", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(candidate, "ProgramBlock", "CompileUnit", "NetworkSource");
    }

    private static bool IsTagsNode(TiaProjectObjectNode node, string candidate)
    {
        return node.ObjectType.Contains("Tag", StringComparison.OrdinalIgnoreCase)
            || node.QualifiedPath.Contains("/Tag", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(candidate, "TagTable", "TagList", "Symbol");
    }

    private static bool IsDataTypesNode(TiaProjectObjectNode node, string candidate)
    {
        return node.ObjectType.Contains("UDT", StringComparison.OrdinalIgnoreCase)
            || node.ObjectType.Contains("DataType", StringComparison.OrdinalIgnoreCase)
            || node.ObjectType.Equals("Type", StringComparison.OrdinalIgnoreCase)
            || node.QualifiedPath.Contains("/TypeGroup", StringComparison.OrdinalIgnoreCase)
            || node.QualifiedPath.Contains("/Types/", StringComparison.OrdinalIgnoreCase)
            || node.QualifiedPath.Contains("/UDT", StringComparison.OrdinalIgnoreCase)
            || ContainsAny(candidate, "UserDataType", "StructType", "PlcStruct", "TypeVersion");
    }

    private static string[] GetDomainAliases(string domain)
    {
        return domain switch
        {
            "PLC.Blocks" => ["PLC.Blocks", "Blocks", "Block"],
            "PLC.Tags" => ["PLC.Tags", "Tags", "Tag"],
            "PLC.DataTypes" => ["PLC.DataTypes", "UDT", "DataType", "Types", "Udts"],
            "UsersAudit" => ["UsersAudit", "Audit", "User"],
            _ => [domain]
        };
    }

    private static bool ContainsAny(string candidate, params string[] terms) =>
        terms.Any(term => candidate.Contains(term, StringComparison.OrdinalIgnoreCase));
}
