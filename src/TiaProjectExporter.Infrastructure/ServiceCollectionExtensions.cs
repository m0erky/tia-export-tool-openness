using Microsoft.Extensions.DependencyInjection;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Infrastructure.Writers;

namespace TiaProjectExporter.Infrastructure;

/// <summary>
/// Dependency injection registrations for infrastructure components.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers infrastructure services.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IExportArtifactWriterFactory, FileSystemExportArtifactWriterFactory>();
        return services;
    }
}
