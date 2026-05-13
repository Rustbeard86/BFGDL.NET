using System.IO;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET.Services;

/// <summary>
/// Writes all log entries (Debug and above) to a rolling plain-text file.
/// Each application launch overwrites the previous log.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileLoggerProvider(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _writer = new StreamWriter(filePath, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };

        _writer.WriteLine($"=== BFGDL.NET log started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ===");
    }

    public ILogger CreateLogger(string categoryName)
        => new FileLogger(categoryName, _writer, _lock);

    public void Dispose()
    {
        lock (_lock)
        {
            try { _writer.Dispose(); } catch { }
        }
    }

    private sealed class FileLogger(string category, StreamWriter writer, object writeLock) : ILogger
    {
        private static readonly string[] LevelLabels =
        [
            "TRC", "DBG", "INF", "WRN", "ERR", "CRT", "NON"
        ];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var level = LevelLabels[(int)logLevel];
            var message = formatter(state, exception);

            // Trim namespace prefix from category for readability
            var cat = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;

            var line = $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [{level}] [{cat}] {message}";
            if (exception is not null)
                line += Environment.NewLine + exception.ToString();

            lock (writeLock)
            {
                try { writer.WriteLine(line); }
                catch { /* best-effort — never throw inside a logger */ }
            }
        }
    }
}
