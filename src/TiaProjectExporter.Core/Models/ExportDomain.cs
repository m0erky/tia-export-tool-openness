namespace TiaProjectExporter.Core.Models;

/// <summary>
/// High-level export domains that can be selected before export.
/// </summary>
public enum ExportDomain
{
    /// <summary>
    /// Project/root level artifacts and metadata.
    /// </summary>
    Project,

    /// <summary>
    /// Hardware and device related objects.
    /// </summary>
    Hardware,

    /// <summary>
    /// Network and communication topology objects.
    /// </summary>
    Network,

    /// <summary>
    /// PLC software-level objects that are not otherwise specialized.
    /// </summary>
    Plc,

    /// <summary>
    /// PLC blocks (OB/FB/FC/DB/instance DB/source).
    /// </summary>
    Blocks,

    /// <summary>
    /// PLC tags and tag tables.
    /// </summary>
    Tags,

    /// <summary>
    /// PLC data types / UDTs.
    /// </summary>
    Udts,

    /// <summary>
    /// Technology and motion related objects.
    /// </summary>
    Technology,

    /// <summary>
    /// Libraries and master copies.
    /// </summary>
    Libraries,

    /// <summary>
    /// HMI objects.
    /// </summary>
    Hmi,

    /// <summary>
    /// Diagnostics, users, audit, safety and health information.
    /// </summary>
    Diagnostics,

    /// <summary>
    /// Catch-all metadata domain.
    /// </summary>
    Metadata
}

