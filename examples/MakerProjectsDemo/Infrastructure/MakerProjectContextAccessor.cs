using System.Collections.Concurrent;

namespace MakerProjectsDemo.Infrastructure;

public interface IMakerProjectContextAccessor
{
    string? CurrentProjectId { get; }
    IDisposable Push(string projectId);
}

public sealed class MakerProjectContextAccessor : IMakerProjectContextAccessor
{
    private readonly AsyncLocal<ConcurrentStack<string>> _contextStack = new();

    public string? CurrentProjectId => _contextStack.Value is { Count: > 0 } stack
        ? stack.TryPeek(out var value)
            ? value
            : null
        : null;

    public IDisposable Push(string projectId)
    {
        var stack = _contextStack.Value ??= new ConcurrentStack<string>();
        stack.Push(projectId);
        return new PopScope(stack);
    }

    private sealed class PopScope : IDisposable
    {
        private readonly ConcurrentStack<string> _stack;
        private bool _disposed;

        public PopScope(ConcurrentStack<string> stack)
        {
            _stack = stack;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stack.TryPop(out _);
        }
    }
}

