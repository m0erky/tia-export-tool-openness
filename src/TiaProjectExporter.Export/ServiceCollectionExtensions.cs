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
        services.AddSingleton<IExportStage, ProjectInventoryStage>();
        services.AddSingleton<IExportStage, BlockCallGraphStage>();
        services.AddSingleton<IExportStage, DependencyGraphStage>();
        services.AddSingleton<IExportStage, RuntimeTypeCatalogStage>();
        services.AddSingleton<IExportStage, TypedExtractorBacklogStage>();
        services.AddSingleton<IExportStage, RelationshipInsightsStage>();
        services.AddSingleton<IExportStage, ExportReadinessStage>();
        services.AddSingleton<IExportStage, NextBestActionsStage>();
        services.AddSingleton<IExportStage, ObjectUsageAnalysisStage>();
        services.AddSingleton<IExportStage, MultilingualTextStage>();
        services.AddSingleton<IExportStage, ExportCoverageMatrixStage>();
        services.AddSingleton<IExportStage, ExportReportStage>();
        services.AddSingleton<IExportStage, ExportIndexStage>();
        services.AddSingleton<IExportStage, CompressionStage>();
        return services;
    }
}
