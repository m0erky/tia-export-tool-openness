using Microsoft.Extensions.DependencyInjection;
using TiaProjectExporter.Application.Services;

namespace TiaProjectExporter.Application;

/// <summary>
/// Dependency injection registrations for application services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers application-layer services.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<ExportCoordinator>();
        return services;
    }
}

