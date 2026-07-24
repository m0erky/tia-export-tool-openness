using Microsoft.Extensions.Logging;

namespace TiaProjectExporter.UI.Logging;

/// <summary>
/// Logger provider that mirrors structured logs into the desktop UI.
/// </summary>
public sealed class UiLoggerProvider : ILoggerProvider
{
    private readonly UiLogCollector _collector;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiLoggerProvider"/> class.
    /// </summary>
    public UiLoggerProvider(UiLogCollector collector)
    {
        _collector = collector;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new UiLogger(categoryName, _collector);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class UiLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly UiLogCollector _collector;

        public UiLogger(string categoryName, UiLogCollector collector)
        {
            _categoryName = categoryName;
            _collector = collector;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{DateTime.Now:HH:mm:ss} [{logLevel}] {_categoryName}: {formatter(state, exception)}";

            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            _collector.Add(message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

