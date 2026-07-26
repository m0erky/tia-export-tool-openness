namespace TiaProjectExporter.UI.Logging;

/// <summary>
/// In-memory sink used to surface log messages in the WPF UI.
/// </summary>
public sealed class UiLogCollector
{
    private readonly object _sync = new();
    private readonly List<string> _entries = [];

    /// <summary>
    /// Raised when a new log message is available.
    /// </summary>
    public event EventHandler<string>? EntryAdded;

    /// <summary>
    /// Gets a snapshot of collected entries.
    /// </summary>
    public IReadOnlyList<string> Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    /// Publishes a log message to UI listeners.
    /// </summary>
    public void Add(string message)
    {
        lock (_sync)
        {
            _entries.Add(message);
        }

        EntryAdded?.Invoke(this, message);
    }
}
