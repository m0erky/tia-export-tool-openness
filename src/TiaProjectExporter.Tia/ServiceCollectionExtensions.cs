using Microsoft.Extensions.DependencyInjection;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Tia.Discovery;

namespace TiaProjectExporter.Tia;

/// <summary>
/// Dependency injection registrations for TIA integration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers TIA-specific services.
    /// </summary>
    public static IServiceCollection AddTiaServices(this IServiceCollection services)
    {
        services.AddSingleton<ITiaInstallationDiscoveryService, RegistryTiaInstallationDiscoveryService>();
        return services;
    }
}
