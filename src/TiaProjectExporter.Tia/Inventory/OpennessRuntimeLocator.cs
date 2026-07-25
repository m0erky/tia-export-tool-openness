namespace TiaProjectExporter.Tia.Inventory;

/// <summary>
/// Resolves Siemens Openness runtime file locations for a TIA installation.
/// </summary>
public static class OpennessRuntimeLocator
{
    private static readonly IReadOnlyList<string> AssemblyCandidateDirectories =
    [
        string.Empty,
        "Bin",
        "PublicAPI",
        "PublicAPI\\V18",
        "PublicAPI\\V19",
        "PublicAPI\\V20"
    ];

    /// <summary>
    /// Attempts to locate <c>Siemens.Engineering.dll</c> inside the provided installation path.
    /// </summary>
    public static string? ResolveEngineeringAssemblyPath(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        foreach (var candidateDirectory in AssemblyCandidateDirectories)
        {
            var candidatePath = string.IsNullOrWhiteSpace(candidateDirectory)
                ? Path.Combine(installPath, "Siemens.Engineering.dll")
                : Path.Combine(installPath, candidateDirectory, "Siemens.Engineering.dll");

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether the provided path looks like a TIA V20 installation root.
    /// </summary>
    public static bool IsLikelyV20InstallationPath(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return false;
        }

        if (installPath.Contains("V20", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains("Portal V20", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Directory.Exists(Path.Combine(installPath, "PublicAPI", "V20"));
    }
}
