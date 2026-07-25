namespace TiaProjectExporter.UI.Services;

/// <summary>
/// Provides folder selection dialogs for the desktop UI.
/// </summary>
public interface IFolderSelectionService
{
    /// <summary>
    /// Opens a folder picker and returns the selected path.
    /// </summary>
    string? PickFolder(string? initialPath);
}

