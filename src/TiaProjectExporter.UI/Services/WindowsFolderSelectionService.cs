using System.Windows.Forms;

namespace TiaProjectExporter.UI.Services;

/// <summary>
/// Windows implementation of folder selection dialogs.
/// </summary>
public sealed class WindowsFolderSelectionService : IFolderSelectionService
{
    /// <inheritdoc />
    public string? PickFolder(string? initialPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the TIA installation root folder",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(initialPath) ? string.Empty : initialPath,
            ShowNewFolderButton = false,
            AutoUpgradeEnabled = true
        };

        var result = dialog.ShowDialog();
        return result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? dialog.SelectedPath
            : null;
    }
}
