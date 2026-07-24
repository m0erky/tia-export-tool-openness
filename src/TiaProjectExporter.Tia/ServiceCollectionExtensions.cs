using Microsoft.Extensions.DependencyInjection;
using TiaProjectExporter.Application.Abstractions;
using TiaProjectExporter.Tia.Discovery;
using TiaProjectExporter.Tia.Inventory;
using TiaProjectExporter.Tia.Inventory.Extraction;

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
        services.AddSingleton<ITiaDomainExtractor, TechnologyDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, HardwareDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, NetworkDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, LibraryDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, HmiScreenFaceplateDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, HmiRecipeAlarmScriptDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, DiagnosticsDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, UsersAuditDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, PlcBlockDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, PlcTagDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, PlcDataTypeDomainExtractor>();
        services.AddSingleton<ITiaDomainExtractor, HmiDomainExtractor>();
        services.AddSingleton<ITiaProjectOpennessAdapter, ReflectionTiaProjectOpennessAdapter>();
        services.AddSingleton<ITiaProjectInventoryProvider, OpennessBackedTiaProjectInventoryProvider>();
        return services;
    }
}
