namespace TiaProjectExporter.Tia.Inventory;

internal static class OutOfProcessHostLocator
{
    public const string HostPathEnvironmentVariable = "TIA_EXPORTER_OPENNESS_HOST_PATH";

    public static string? ResolveHostExecutablePath()
    {
        var environmentPath = Environment.GetEnvironmentVariable(HostPathEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return environmentPath;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var directCandidate = Path.Combine(baseDirectory, "TiaProjectExporter.OpennessHost.exe");

        if (File.Exists(directCandidate))
        {
            return directCandidate;
        }

        var nestedCandidate = Path.Combine(baseDirectory, "OpennessHost", "TiaProjectExporter.OpennessHost.exe");

        return File.Exists(nestedCandidate)
            ? nestedCandidate
            : null;
    }
}

