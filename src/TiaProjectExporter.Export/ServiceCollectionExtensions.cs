using Microsoft.Extensions.DependencyInjection;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Export.Stages;

namespace TiaProjectExporter.Export;

/// <summary>
/// Dependency injection registrations for export-specific services and stages.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers export services.
    /// </summary>
    public static IServiceCollection AddExportServices(this IServiceCollection services)
    {
        services.AddSingleton<IExportStage, RepositoryLayoutStage>();
        return services;
    }
}

