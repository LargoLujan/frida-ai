using Microsoft.Extensions.Logging;

namespace TeamsJarvis.Core;

/// <summary>Logger mínimo a archivo diario en %LOCALAPPDATA%\TeamsJarvis\logs.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly object Gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName);

    public void Dispose() { }

    private sealed class FileLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null) line += Environment.NewLine + exception;
            var path = Path.Combine(AppPaths.LogsDir, $"jarvis-{DateTime.Now:yyyyMMdd}.log");
            lock (Gate)
            {
                try { File.AppendAllText(path, line + Environment.NewLine); }
                catch { /* nunca tumbar la app por logging */ }
            }
        }
    }
}
