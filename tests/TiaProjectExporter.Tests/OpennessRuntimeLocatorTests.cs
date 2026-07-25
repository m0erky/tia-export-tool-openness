using TiaProjectExporter.Tia.Inventory;

namespace TiaProjectExporter.Tests;

public sealed class OpennessRuntimeLocatorTests
{
    [Fact]
    public void ResolveEngineeringAssemblyPath_ReturnsRootAssembly_WhenPresent()
    {
        var installationRoot = CreateTempDirectory();

        try
        {
            var expectedAssemblyPath = Path.Combine(installationRoot, "Siemens.Engineering.dll");
            File.WriteAllText(expectedAssemblyPath, string.Empty);

            var actualAssemblyPath = OpennessRuntimeLocator.ResolveEngineeringAssemblyPath(installationRoot);

            Assert.Equal(expectedAssemblyPath, actualAssemblyPath);
        }
        finally
        {
            Directory.Delete(installationRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveEngineeringAssemblyPath_ReturnsPublicApiAssembly_WhenPresent()
    {
        var installationRoot = CreateTempDirectory();

        try
        {
            var publicApiDirectory = Path.Combine(installationRoot, "PublicAPI", "V20");
            Directory.CreateDirectory(publicApiDirectory);
            var expectedAssemblyPath = Path.Combine(publicApiDirectory, "Siemens.Engineering.dll");
            File.WriteAllText(expectedAssemblyPath, string.Empty);

            var actualAssemblyPath = OpennessRuntimeLocator.ResolveEngineeringAssemblyPath(installationRoot);

            Assert.Equal(expectedAssemblyPath, actualAssemblyPath);
        }
        finally
        {
            Directory.Delete(installationRoot, recursive: true);
        }
    }

    [Fact]
    public void IsLikelyV20InstallationPath_ReturnsTrue_WhenPublicApiV20Exists()
    {
        var installationRoot = CreateTempDirectory();

        try
        {
            Directory.CreateDirectory(Path.Combine(installationRoot, "PublicAPI", "V20"));

            var result = OpennessRuntimeLocator.IsLikelyV20InstallationPath(installationRoot);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(installationRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tia-exporter-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

