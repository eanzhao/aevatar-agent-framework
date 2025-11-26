using MakerProjectsDemo.Infrastructure;
using Microsoft.Extensions.Logging;

namespace MakerProjectsDemo.Infrastructure;

public sealed class TimelineLoggerProvider : ILoggerProvider
{
    private readonly MakerTimelineHub _timelineHub;
    private readonly IMakerProjectContextAccessor _contextAccessor;

    public TimelineLoggerProvider(
        MakerTimelineHub timelineHub,
        IMakerProjectContextAccessor contextAccessor)
    {
        _timelineHub = timelineHub;
        _contextAccessor = contextAccessor;
    }

    public ILogger CreateLogger(string categoryName) => new TimelineLogger(_timelineHub, _contextAccessor, categoryName);

    public void Dispose()
    {
    }

    private sealed class TimelineLogger : ILogger
    {
        private readonly MakerTimelineHub _hub;
        private readonly IMakerProjectContextAccessor _contextAccessor;
        private readonly string _category;

        public TimelineLogger(
            MakerTimelineHub hub,
            IMakerProjectContextAccessor contextAccessor,
            string category)
        {
            _hub = hub;
            _contextAccessor = contextAccessor;
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
                   _category.Contains("Agent", StringComparison.OrdinalIgnoreCase);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var projectId = _contextAccessor.CurrentProjectId;
            
            // DEBUG: Always print to console to verify generation
            if (message.Contains("WORKER_STREAM"))
            {
                Console.WriteLine($"[STREAM-DEBUG] Proj:{projectId ?? "NULL"} | {message}");
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                return;
            }

            _hub.GetStore(projectId).Append(logLevel.ToString(), _category, message);
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
