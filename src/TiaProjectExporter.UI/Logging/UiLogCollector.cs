namespace TiaProjectExporter.UI.Logging;

/// <summary>
/// In-memory sink used to surface log messages in the WPF UI.
/// </summary>
public sealed class UiLogCollector
{
    /// <summary>
    /// Raised when a new log message is available.
    /// </summary>
    public event EventHandler<string>? EntryAdded;

    /// <summary>
    /// Publishes a log message to UI listeners.
    /// </summary>
    public void Add(string message) => EntryAdded?.Invoke(this, message);
}

