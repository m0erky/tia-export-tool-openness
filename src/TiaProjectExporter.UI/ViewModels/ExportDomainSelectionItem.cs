using TiaProjectExporter.Core.Models;

namespace TiaProjectExporter.UI.ViewModels;

/// <summary>
/// Selectable export-domain item used in the pre-scan UI.
/// </summary>
public sealed class ExportDomainSelectionItem : ObservableObject
{
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExportDomainSelectionItem"/> class.
    /// </summary>
    public ExportDomainSelectionItem(ExportDomain domain, int objectCount, bool isSelected)
    {
        Domain = domain;
        ObjectCount = objectCount;
        _isSelected = isSelected;
    }

    /// <summary>
    /// Gets the domain represented by this item.
    /// </summary>
    public ExportDomain Domain { get; }

    /// <summary>
    /// Gets the discovered object count in this domain.
    /// </summary>
    public int ObjectCount { get; }

    /// <summary>
    /// Gets a display name for the domain.
    /// </summary>
    public string DisplayName => $"{TiaInventoryDomainClassifier.ToFolderName(Domain)} ({ObjectCount})";

    /// <summary>
    /// Gets or sets a value indicating whether this domain should be exported.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }
}

