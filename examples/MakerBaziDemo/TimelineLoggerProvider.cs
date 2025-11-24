using Microsoft.Extensions.Logging;

namespace MakerBaziDemo;

public sealed class TimelineLoggerProvider : ILoggerProvider
{
    private readonly MakerTimelineStore _store;

    public TimelineLoggerProvider(MakerTimelineStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) => new TimelineLogger(_store, categoryName);

    public void Dispose()
    {
    }

    private sealed class TimelineLogger : ILogger
    {
        private readonly MakerTimelineStore _store;
        private readonly string _category;

        public TimelineLogger(MakerTimelineStore store, string category)
        {
            _store = store;
            _category = category;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel < LogLevel.Information)
            {
                return false;
            }

            return _category.Contains("Maker", StringComparison.OrdinalIgnoreCase) ||
                   _category.Contains("Bazi", StringComparison.OrdinalIgnoreCase) ||
                   _category.Contains("MakerBaziDemo", StringComparison.OrdinalIgnoreCase);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            _store.Append(logLevel.ToString(), _category, message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

