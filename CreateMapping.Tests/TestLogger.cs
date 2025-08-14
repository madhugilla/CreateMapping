using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace CreateMapping.Tests;

public sealed class TestLogger<T> : ILogger<T>, IDisposable
{
    private readonly ConcurrentQueue<string> _messages = new();
    public IDisposable BeginScope<TState>(TState state) => this;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Dispose() { }
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        var msg = $"[{logLevel}] {formatter(state, exception)}";
        if (exception != null) msg += " EX: " + exception.GetType().Name;
        _messages.Enqueue(msg);
    }
    public string[] Snapshot() => _messages.ToArray();
    public bool Contains(string fragment) => Snapshot().Any(m => m.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
